using BackendApi.Modules.Cms.Authorization;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Editor.SaveBannerDraft;

public static class SaveBannerDraftEndpoint
{
    public static IEndpointRouteBuilder MapSaveBannerDraftEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/banner-slots/drafts", CreateAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapPatch("/banner-slots/drafts/{id:guid}", UpdateAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static Task<IResult> CreateAsync(
        [FromBody] SaveBannerDraftRequest? body,
        HttpContext context,
        [FromServices] SaveBannerDraftHandler handler,
        CancellationToken ct) => HandleCoreAsync(id: null, body, context, handler, ct);

    private static Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] SaveBannerDraftRequest? body,
        HttpContext context,
        [FromServices] SaveBannerDraftHandler handler,
        CancellationToken ct) => HandleCoreAsync(id, body, context, handler, ct);

    private static async Task<IResult> HandleCoreAsync(
        Guid? id,
        SaveBannerDraftRequest? body,
        HttpContext context,
        SaveBannerDraftHandler handler,
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

        var (ok, reason, detail) = SaveBannerDraftValidator.Validate(body);
        if (!ok)
        {
            return CmsResponseFactory.Problem(context, 400, reason!, "Banner draft validation failed.", detail);
        }

        var actorRole = CmsResponseFactory.ResolveActorRole(context);
        var result = await handler.HandleAsync(id, actorId.Value, actorRole, body!, ct);

        if (!result.IsSuccess)
        {
            return CmsResponseFactory.Problem(context, result.Status, result.ReasonCode!,
                "Banner draft save rejected.", result.Detail);
        }

        return result.Status == 201
            ? Results.Created($"/v1/admin/cms/banner-slots/drafts/{result.Row!.Id}", result.Row)
            : Results.Ok(result.Row);
    }
}
