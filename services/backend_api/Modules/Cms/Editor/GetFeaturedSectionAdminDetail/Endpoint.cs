using BackendApi.Modules.Cms.Authorization;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Editor.GetFeaturedSectionAdminDetail;

/// <summary>
/// GET /v1/admin/cms/featured-sections/{id} per spec 024 task T122.
/// Editor-permission-gated; returns admin-side detail with per-reference
/// availability badges sourced live from catalog read contracts.
/// </summary>
public static class GetFeaturedSectionAdminDetailEndpoint
{
    public static IEndpointRouteBuilder MapGetFeaturedSectionAdminDetailEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/featured-sections/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        [FromServices] GetFeaturedSectionAdminDetailHandler handler,
        CancellationToken ct)
    {
        if (!CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.Editor))
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.EditorRoleRequired, "cms.editor permission required.");
        }
        if (CmsResponseFactory.ResolveActorId(context) is null)
        {
            return CmsResponseFactory.Problem(context, 401,
                CmsReasonCode.EditorRoleRequired, "Authenticated actor required.");
        }

        var response = await handler.HandleAsync(new GetFeaturedSectionAdminDetailQuery(id), ct);
        if (response is null)
        {
            return CmsResponseFactory.Problem(context, 404,
                CmsReasonCode.PreviewEntityNotFound, "Featured section not found.");
        }
        return Results.Ok(response);
    }
}
