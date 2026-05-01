using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.SuperAdmin;

public sealed record EditMarketSchemaRequest(
    int? BannerMaxLivePerSlot,
    int? FeaturedSectionMaxReferences,
    int? PreviewTokenDefaultTtlHours,
    int? DraftStalenessAlertDays,
    int? AssetGracePeriodDays,
    uint? Xmin);

public sealed record OrphanedAssetItem(
    Guid Id,
    string MimeType,
    long SizeBytes,
    string? IntendedLocale,
    string? OriginalFilename,
    DateTimeOffset? DereferencedAtUtc,
    DateTimeOffset UploadedAtUtc);

public static class SuperAdminEndpoints
{
    public static IEndpointRouteBuilder MapSuperAdminEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPatch("/market-schemas/{marketCode}", EditMarketSchemaAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapGet("/orphaned-assets", ListOrphanedAssetsAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapGet("/metrics", MetricsAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> EditMarketSchemaAsync(
        string marketCode,
        [FromBody] EditMarketSchemaRequest? body,
        HttpContext context,
        [FromServices] CmsDbContext db,
        [FromServices] IAuditEventPublisher audit,
        CancellationToken ct)
    {
        if (!CmsResponseFactory.HasPermissionClaim(context, "super_admin"))
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.LegalPageCrossMarketRequiresSuperAdmin,
                "super_admin permission required.");
        }
        if (body is null)
        {
            return CmsResponseFactory.Problem(context, 400,
                CmsReasonCode.MarketSchemaValueOutOfRange, "Request body is required.");
        }
        var actorId = CmsResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return CmsResponseFactory.Problem(context, 401,
                CmsReasonCode.LegalPageCrossMarketRequiresSuperAdmin,
                "Authenticated actor required.");
        }

        var schema = await db.MarketSchemas.FirstOrDefaultAsync(s => s.MarketCode == marketCode, ct);
        if (schema is null)
        {
            return CmsResponseFactory.Problem(context, 404,
                CmsReasonCode.MarketSchemaValueOutOfRange,
                $"Market schema '{marketCode}' not found.");
        }
        if (body.Xmin is null || schema.Xmin != body.Xmin.Value)
        {
            return CmsResponseFactory.Problem(context, 409,
                CmsReasonCode.MarketSchemaVersionConflict,
                "market_schema row was modified since it was loaded.");
        }

        // Per data-model §2.9 CHECK constraints — bounds checks.
        if (body.BannerMaxLivePerSlot is { } bm && (bm < 1 || bm > 50))
        {
            return CmsResponseFactory.Problem(context, 400,
                CmsReasonCode.MarketSchemaValueOutOfRange,
                "banner_max_live_per_slot must be in [1, 50].");
        }
        if (body.FeaturedSectionMaxReferences is { } fr && (fr < 1 || fr > 100))
        {
            return CmsResponseFactory.Problem(context, 400,
                CmsReasonCode.MarketSchemaValueOutOfRange,
                "featured_section_max_references must be in [1, 100].");
        }
        if (body.PreviewTokenDefaultTtlHours is { } ttl && (ttl < 1 || ttl > 168))
        {
            return CmsResponseFactory.Problem(context, 400,
                CmsReasonCode.MarketSchemaValueOutOfRange,
                "preview_token_default_ttl_hours must be in [1, 168].");
        }
        if (body.DraftStalenessAlertDays is { } sd && (sd < 7 || sd > 365))
        {
            return CmsResponseFactory.Problem(context, 400,
                CmsReasonCode.MarketSchemaValueOutOfRange,
                "draft_staleness_alert_days must be in [7, 365].");
        }
        if (body.AssetGracePeriodDays is { } ag && (ag < 1 || ag > 90))
        {
            return CmsResponseFactory.Problem(context, 400,
                CmsReasonCode.MarketSchemaValueOutOfRange,
                "asset_grace_period_days must be in [1, 90].");
        }

        if (body.BannerMaxLivePerSlot is int v1) schema.BannerMaxLivePerSlot = v1;
        if (body.FeaturedSectionMaxReferences is int v2) schema.FeaturedSectionMaxReferences = v2;
        if (body.PreviewTokenDefaultTtlHours is int v3) schema.PreviewTokenDefaultTtlHours = v3;
        if (body.DraftStalenessAlertDays is int v4) schema.DraftStalenessAlertDays = v4;
        if (body.AssetGracePeriodDays is int v5) schema.AssetGracePeriodDays = v5;
        schema.LastEditedByActorId = actorId.Value;
        schema.LastEditedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CmsResponseFactory.Problem(context, 409,
                CmsReasonCode.MarketSchemaVersionConflict,
                "market_schema row was modified concurrently.");
        }

        await audit.PublishAsync(new AuditEvent(
            ActorId: actorId.Value,
            ActorRole: CmsResponseFactory.ResolveActorRole(context),
            Action: "cms.market_schema.updated",
            EntityType: "cms.market_schema",
            EntityId: Guid.Empty,
            BeforeState: null,
            AfterState: new
            {
                schema.MarketCode,
                schema.BannerMaxLivePerSlot,
                schema.FeaturedSectionMaxReferences,
                schema.PreviewTokenDefaultTtlHours,
                schema.DraftStalenessAlertDays,
                schema.AssetGracePeriodDays,
                schema.Xmin,
            },
            Reason: null), ct);

        return Results.Ok(schema);
    }

    private static async Task<IResult> ListOrphanedAssetsAsync(
        HttpContext context,
        [FromServices] CmsDbContext db,
        CancellationToken ct)
    {
        if (!CmsResponseFactory.HasPermissionClaim(context, "super_admin"))
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.LegalPageCrossMarketRequiresSuperAdmin,
                "super_admin permission required.");
        }

        var rows = await db.Assets.AsNoTracking()
            .Where(a => a.StorageObjectStateWire == "active" && a.DereferencedAtUtc != null)
            .OrderBy(a => a.DereferencedAtUtc)
            .Take(500)
            .ToListAsync(ct);

        var items = rows.Select(a => new OrphanedAssetItem(
            a.Id, a.Mime, a.SizeBytes, a.IntendedLocale,
            a.OriginalFilename, a.DereferencedAtUtc, a.UploadedAtUtc)).ToList();

        return Results.Ok(new { items });
    }

    private static async Task<IResult> MetricsAsync(
        HttpContext context,
        [FromServices] CmsDbContext db,
        CancellationToken ct)
    {
        var canRead =
            CmsResponseFactory.HasPermissionClaim(context, "super_admin") ||
            CmsResponseFactory.HasPermissionClaim(context, "cms.viewer.finance");
        if (!canRead)
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.LegalPageCrossMarketRequiresSuperAdmin,
                "super_admin or cms.viewer.finance permission required.");
        }

        var bannerCounts = await db.BannerSlots.AsNoTracking()
            .GroupBy(b => b.StateWire)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var featuredCounts = await db.FeaturedSections.AsNoTracking()
            .GroupBy(f => f.StateWire)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var faqCounts = await db.FaqEntries.AsNoTracking()
            .GroupBy(f => f.StateWire)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var blogCounts = await db.BlogArticles.AsNoTracking()
            .GroupBy(b => b.StateWire)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var legalCounts = await db.LegalPageVersions.AsNoTracking()
            .GroupBy(l => l.StateWire)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var previewActive = await db.PreviewTokens.AsNoTracking()
            .CountAsync(t => t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTimeOffset.UtcNow, ct);
        var previewRevoked = await db.PreviewTokens.AsNoTracking()
            .CountAsync(t => t.RevokedAtUtc != null, ct);
        var assetsActive = await db.Assets.AsNoTracking()
            .CountAsync(a => a.StorageObjectStateWire == "active", ct);
        var assetsSwept = await db.Assets.AsNoTracking()
            .CountAsync(a => a.StorageObjectStateWire == "swept", ct);
        var assetsInGrace = await db.Assets.AsNoTracking()
            .CountAsync(a => a.StorageObjectStateWire == "active" && a.DereferencedAtUtc != null, ct);

        return Results.Ok(new
        {
            banner_slot = bannerCounts,
            featured_section = featuredCounts,
            faq_entry = faqCounts,
            blog_article = blogCounts,
            legal_page_version = legalCounts,
            preview_tokens = new { active = previewActive, revoked = previewRevoked },
            assets = new { active = assetsActive, swept = assetsSwept, in_grace = assetsInGrace },
        });
    }
}
