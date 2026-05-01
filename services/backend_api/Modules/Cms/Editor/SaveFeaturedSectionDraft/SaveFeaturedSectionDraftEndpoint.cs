using BackendApi.Modules.Cms.Authorization;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Editor.SaveFeaturedSectionDraft;

public static class SaveFeaturedSectionDraftEndpoint
{
    public static IEndpointRouteBuilder MapSaveFeaturedSectionDraftEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/featured-sections/drafts", (
                [FromBody] SaveFeaturedSectionDraftRequest? body,
                HttpContext context,
                [FromServices] SaveFeaturedSectionDraftHandler handler,
                [FromServices] CmsDbContext db,
                CancellationToken ct) => Core(null, body, context, handler, db, ct))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapPatch("/featured-sections/drafts/{id:guid}", (
                Guid id,
                [FromBody] SaveFeaturedSectionDraftRequest? body,
                HttpContext context,
                [FromServices] SaveFeaturedSectionDraftHandler handler,
                [FromServices] CmsDbContext db,
                CancellationToken ct) => Core(id, body, context, handler, db, ct))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> Core(
        Guid? id,
        SaveFeaturedSectionDraftRequest? body,
        HttpContext context,
        SaveFeaturedSectionDraftHandler handler,
        CmsDbContext db,
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
        var maxRefs = await ResolveMaxReferencesAsync(db, body.MarketCode, ct);
        var result = await handler.HandleAsync(id, actorId.Value, actorRole, body, maxRefs, ct);
        if (!result.IsSuccess)
        {
            return CmsResponseFactory.Problem(context, result.Status, result.ReasonCode!,
                "Featured section draft save rejected.", result.Detail);
        }
        return result.Status == 201
            ? Results.Created($"/v1/admin/cms/featured-sections/drafts/{result.Row!.Id}", result.Row)
            : Results.Ok(result.Row);
    }

    private static async Task<int> ResolveMaxReferencesAsync(CmsDbContext db, string marketCode, CancellationToken ct)
    {
        var schema = await db.MarketSchemas.AsNoTracking()
            .FirstOrDefaultAsync(s => s.MarketCode == marketCode, ct);
        return schema?.FeaturedSectionMaxReferences ?? 24;
    }
}
