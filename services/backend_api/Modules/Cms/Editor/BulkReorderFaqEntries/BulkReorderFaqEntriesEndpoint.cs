using BackendApi.Modules.Cms.Authorization;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Editor.BulkReorderFaqEntries;

public static class BulkReorderFaqEntriesEndpoint
{
    public static IEndpointRouteBuilder MapBulkReorderFaqEntriesEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/faq-entries/reorder", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] BulkReorderFaqEntriesRequest? body,
        HttpContext context,
        [FromServices] BulkReorderFaqEntriesHandler handler,
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
        var result = await handler.HandleAsync(body, actorId.Value, actorRole, ct);
        if (!result.IsSuccess)
        {
            return CmsResponseFactory.Problem(context, result.Status, result.ReasonCode!,
                "FAQ reorder rejected.", result.Detail,
                new Dictionary<string, object?> { ["conflict_rows"] = result.ConflictRows });
        }
        return Results.Ok(new { updated = result.UpdatedCount });
    }
}
