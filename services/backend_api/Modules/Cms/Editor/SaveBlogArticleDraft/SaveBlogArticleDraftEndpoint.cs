using BackendApi.Modules.Cms.Authorization;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Editor.SaveBlogArticleDraft;

public static class SaveBlogArticleDraftEndpoint
{
    public static IEndpointRouteBuilder MapSaveBlogArticleDraftEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/blog-articles/drafts", CreateAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapPatch("/blog-articles/drafts/{id:guid}", UpdateAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static Task<IResult> CreateAsync(
        [FromBody] SaveBlogArticleDraftRequest? body,
        HttpContext context,
        [FromServices] SaveBlogArticleDraftHandler handler,
        CancellationToken ct) => HandleCoreAsync(null, body, context, handler, ct);

    private static Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] SaveBlogArticleDraftRequest? body,
        HttpContext context,
        [FromServices] SaveBlogArticleDraftHandler handler,
        CancellationToken ct) => HandleCoreAsync(id, body, context, handler, ct);

    private static async Task<IResult> HandleCoreAsync(
        Guid? id,
        SaveBlogArticleDraftRequest? body,
        HttpContext context,
        SaveBlogArticleDraftHandler handler,
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
        var (ok, reason, detail) = SaveBlogArticleDraftValidator.Validate(body);
        if (!ok)
        {
            return CmsResponseFactory.Problem(context, 400, reason!, "Blog article draft validation failed.", detail);
        }
        var actorRole = CmsResponseFactory.ResolveActorRole(context);
        var result = await handler.HandleAsync(id, actorId.Value, actorRole, body!, ct);
        if (!result.IsSuccess)
        {
            return CmsResponseFactory.Problem(context, result.Status, result.ReasonCode!,
                "Blog article draft save rejected.", result.Detail);
        }
        return result.Status == 201
            ? Results.Created($"/v1/admin/cms/blog-articles/drafts/{result.Row!.Id}", result.Row)
            : Results.Ok(result.Row);
    }
}
