using BackendApi.Modules.Cms.Authorization;
using BackendApi.Modules.Cms.LegalOwner.ListLegalPageVersionHistory;
using BackendApi.Modules.Cms.LegalOwner.PublishLegalPageVersionNow;
using BackendApi.Modules.Cms.LegalOwner.SaveLegalPageVersionDraft;
using BackendApi.Modules.Cms.LegalOwner.SchedulePublishLegalPageVersion;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.LegalOwner;

public static class LegalOwnerEndpoints
{
    public static IEndpointRouteBuilder MapLegalOwnerEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/legal-pages/{kind}/versions/drafts", CreateDraftAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapPatch("/legal-pages/{kind}/versions/drafts/{id:guid}", UpdateDraftAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapPost("/legal-pages/{kind}/versions/{id:guid}/schedule-publish", ScheduleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapPost("/legal-pages/{kind}/versions/{id:guid}/publish-now", PublishNowAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapPost("/legal-pages/{kind}/versions/{id:guid}/publish-cross-market", PublishCrossMarketAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapGet("/legal-pages/{kind}/versions", ListHistoryAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapDelete("/legal-pages/{kind}/versions/{id:guid}", DeleteForbiddenAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static Task<IResult> CreateDraftAsync(
        string kind,
        [FromBody] SaveLegalPageVersionDraftRequest? body,
        HttpContext context,
        [FromServices] SaveLegalPageVersionDraftHandler handler,
        CancellationToken ct) => SaveDraftCoreAsync(id: null, kind, body, context, handler, ct);

    private static Task<IResult> UpdateDraftAsync(
        string kind,
        Guid id,
        [FromBody] SaveLegalPageVersionDraftRequest? body,
        HttpContext context,
        [FromServices] SaveLegalPageVersionDraftHandler handler,
        CancellationToken ct) => SaveDraftCoreAsync(id, kind, body, context, handler, ct);

    private static async Task<IResult> SaveDraftCoreAsync(
        Guid? id,
        string kind,
        SaveLegalPageVersionDraftRequest? body,
        HttpContext context,
        SaveLegalPageVersionDraftHandler handler,
        CancellationToken ct)
    {
        var auth = AssertLegalOwnerOrSuperAdmin(context);
        if (auth is { } problem) return problem;

        var (ok, reason, detail) = SaveLegalPageVersionDraftValidator.Validate(kind, body);
        if (!ok)
        {
            return CmsResponseFactory.Problem(context, 400, reason!, "Legal page draft validation failed.", detail);
        }

        var actorId = CmsResponseFactory.ResolveActorId(context)!.Value;
        var actorRole = CmsResponseFactory.ResolveActorRole(context);
        var result = await handler.HandleAsync(id, kind, actorId, actorRole, body!, ct);

        if (!result.IsSuccess)
        {
            return CmsResponseFactory.Problem(context, result.Status, result.ReasonCode!,
                "Legal page draft save rejected.", result.Detail);
        }
        return result.Status == 201
            ? Results.Created($"/v1/admin/cms/legal-pages/{kind}/versions/drafts/{result.Row!.Id}", result.Row)
            : Results.Ok(result.Row);
    }

    private static async Task<IResult> ScheduleAsync(
        string kind,
        Guid id,
        HttpContext context,
        [FromServices] SchedulePublishLegalPageVersionHandler handler,
        CancellationToken ct)
    {
        var auth = AssertLegalOwnerOrSuperAdmin(context);
        if (auth is { } problem) return problem;

        var actorId = CmsResponseFactory.ResolveActorId(context)!.Value;
        var actorRole = CmsResponseFactory.ResolveActorRole(context);
        var isSuper = CmsResponseFactory.HasPermissionClaim(context, "super_admin");
        var result = await handler.HandleAsync(id, kind, actorId, actorRole, isSuper, ct);
        return ToHttp(context, result);
    }

    private static async Task<IResult> PublishNowAsync(
        string kind,
        Guid id,
        HttpContext context,
        [FromServices] PublishLegalPageVersionNowHandler handler,
        CancellationToken ct)
    {
        var auth = AssertLegalOwnerOrSuperAdmin(context);
        if (auth is { } problem) return problem;

        var actorId = CmsResponseFactory.ResolveActorId(context)!.Value;
        var actorRole = CmsResponseFactory.ResolveActorRole(context);
        var isSuper = CmsResponseFactory.HasPermissionClaim(context, "super_admin");
        var result = await handler.HandleAsync(id, kind, actorId, actorRole, isSuper, ct);
        return ToHttp(context, result);
    }

    private static async Task<IResult> PublishCrossMarketAsync(
        string kind,
        Guid id,
        HttpContext context,
        [FromServices] PublishLegalPageVersionNowHandler handler,
        [FromServices] BackendApi.Modules.Cms.Persistence.CmsDbContext db,
        CancellationToken ct)
    {
        if (!CmsResponseFactory.HasPermissionClaim(context, "super_admin"))
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.LegalPageCrossMarketRequiresSuperAdmin,
                "Cross-market (*) legal page publish requires super_admin.");
        }
        if (CmsResponseFactory.ResolveActorId(context) is null)
        {
            return CmsResponseFactory.Problem(context, 401,
                CmsReasonCode.LegalPagePublishRoleRequired, "Authenticated actor required.");
        }

        // Pre-check the row's market_code; if it isn't '*', reject 400.
        var row = await db.LegalPageVersions.FindAsync(new object[] { id }, ct);
        if (row is null)
        {
            return CmsResponseFactory.Problem(context, 404,
                CmsReasonCode.PreviewEntityNotFound, "Legal page version not found.");
        }
        if (row.MarketCode != "*")
        {
            return CmsResponseFactory.Problem(context, 400,
                CmsReasonCode.LegalPageScopeNotCrossMarket,
                "publish-cross-market requires market_code='*'.");
        }
        // Detach so the handler reloads cleanly inside the supersession transaction.
        db.Entry(row).State = Microsoft.EntityFrameworkCore.EntityState.Detached;

        var actorId = CmsResponseFactory.ResolveActorId(context)!.Value;
        var actorRole = CmsResponseFactory.ResolveActorRole(context);
        var result = await handler.HandleAsync(id, kind, actorId, actorRole, isSuperAdmin: true, ct);
        return ToHttp(context, result);
    }

    private static async Task<IResult> ListHistoryAsync(
        string kind,
        HttpContext context,
        [FromServices] ListLegalPageVersionHistoryHandler handler,
        CancellationToken ct,
        [FromQuery] string? market = null)
    {
        if (!IsSupportedLegalKind(kind))
        {
            return CmsResponseFactory.Problem(context, 404,
                CmsReasonCode.LegalPageNotFoundForMarket,
                $"Unsupported legal page kind '{kind}'.");
        }

        // Read RBAC: cms.legal_owner | cms.viewer.finance | super_admin.
        var canRead =
            CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.LegalOwner) ||
            CmsResponseFactory.HasPermissionClaim(context, "cms.viewer.finance") ||
            CmsResponseFactory.HasPermissionClaim(context, "super_admin");
        if (!canRead)
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.LegalPagePublishRoleRequired,
                "cms.legal_owner / cms.viewer.finance / super_admin permission required.");
        }

        if (string.IsNullOrWhiteSpace(market))
        {
            return CmsResponseFactory.Problem(context, 400,
                CmsReasonCode.StorefrontMarketUnsupported, "market query parameter is required.");
        }
        if (market != "EG" && market != "KSA" && market != "*")
        {
            return CmsResponseFactory.Problem(context, 400,
                CmsReasonCode.StorefrontMarketUnsupported, $"Unsupported market '{market}'.");
        }

        var response = await handler.HandleAsync(kind, market, ct);
        return Results.Ok(response);
    }

    private static IResult DeleteForbiddenAsync(string kind, Guid id, HttpContext context)
    {
        // FR-005a: hard-delete is forbidden on every legal page version.
        return CmsResponseFactory.Problem(context, 405,
            CmsReasonCode.LegalPageVersionDeleteForbidden,
            "Hard-delete is forbidden on legal page versions; supersede instead.");
    }

    private static IResult? AssertLegalOwnerOrSuperAdmin(HttpContext context)
    {
        var ok =
            CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.LegalOwner) ||
            CmsResponseFactory.HasPermissionClaim(context, "super_admin");
        if (!ok)
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.LegalPagePublishRoleRequired,
                "cms.legal_owner permission required.");
        }
        if (CmsResponseFactory.ResolveActorId(context) is null)
        {
            return CmsResponseFactory.Problem(context, 401,
                CmsReasonCode.LegalPagePublishRoleRequired, "Authenticated actor required.");
        }
        return null;
    }

    private static IResult ToHttp(HttpContext context, LegalPagePublisherResult result)
    {
        if (!result.IsSuccess)
        {
            return CmsResponseFactory.Problem(context, result.Status, result.ReasonCode!,
                "Legal page publish operation rejected.", result.Detail, result.Extensions);
        }
        return Results.Ok(new
        {
            row = result.Row,
            superseded = result.SupersededRow,
        });
    }

    private static bool IsSupportedLegalKind(string kind) => kind switch
    {
        "terms" or "privacy" or "returns" or "cookies" => true,
        _ => false,
    };
}
