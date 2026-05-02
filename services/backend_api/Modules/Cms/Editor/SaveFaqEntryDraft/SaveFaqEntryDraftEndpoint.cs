using BackendApi.Modules.Cms.Authorization;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Editor.SaveFaqEntryDraft;

public static class SaveFaqEntryDraftEndpoint
{
    public static IEndpointRouteBuilder MapSaveFaqEntryDraftEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/faq-entries/drafts", (
                [FromBody] SaveFaqEntryDraftRequest? body,
                HttpContext context,
                [FromServices] SaveFaqEntryDraftHandler handler,
                CancellationToken ct) => Core(null, body, context, handler, ct))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapPatch("/faq-entries/drafts/{id:guid}", (
                Guid id,
                [FromBody] SaveFaqEntryDraftRequest? body,
                HttpContext context,
                [FromServices] SaveFaqEntryDraftHandler handler,
                CancellationToken ct) => Core(id, body, context, handler, ct))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> Core(
        Guid? id,
        SaveFaqEntryDraftRequest? body,
        HttpContext context,
        SaveFaqEntryDraftHandler handler,
        CancellationToken ct)
    {
        if (!CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.Editor))
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.EditorRoleRequired, "cms.editor permission required.");
        }
        var actorId = CmsResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return CmsResponseFactory.Problem(context, 401,
                CmsReasonCode.EditorRoleRequired, "Authenticated actor required.");
        }
        if (body is null)
        {
            return CmsResponseFactory.Problem(context, 400,
                CmsReasonCode.PublishLocaleCompletenessMissing, "Request body is required.");
        }
        var actorRole = CmsResponseFactory.ResolveActorRole(context);
        var result = await handler.HandleAsync(id, actorId.Value, actorRole, body, ct);
        if (!result.IsSuccess)
        {
            return CmsResponseFactory.Problem(context, result.Status, result.ReasonCode!,
                "FAQ entry draft save rejected.", result.Detail);
        }
        return result.Status == 201
            ? Results.Created($"/v1/admin/cms/faq-entries/drafts/{result.Row!.Id}", result.Row)
            : Results.Ok(result.Row);
    }
}
