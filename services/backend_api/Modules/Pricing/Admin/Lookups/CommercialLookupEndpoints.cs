using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Admin.Lookups;

/// <summary>
/// Spec 007-b commercial-authoring lookup endpoints (contract §10 / FR-020).
/// These thin pickers consume the appropriate upstream module's DbContext and
/// project a uniform shape so the admin UI (spec 015) takes a single auth
/// boundary instead of one per upstream module.
///
/// Performance budgets (research §R11):
/// - SKU picker: p95 ≤ 300 ms against 50 000 SKUs (SC-006).
/// - Company / segment pickers: p95 ≤ 200 ms.
///
/// Pagination is cursor-based (CreatedAt, Id); page cap is 200.
/// </summary>
public static class CommercialLookupEndpoints
{
    private const int DefaultPageSize = 50;
    private const int LookupPageCap = 200;

    public static IEndpointRouteBuilder MapCommercialLookupEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/lookups");
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        group.MapGet("/skus", SearchSkusAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapGet("/companies", SearchCompaniesAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapGet("/segments", SearchSegmentsAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        return builder;
    }

    // -------------------- GET /lookups/skus --------------------

    private static async Task<IResult> SearchSkusAsync(
        [FromQuery] string? q,
        [FromQuery(Name = "market_code")] string? marketCode,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CatalogDbContext catalog,
        CancellationToken ct)
    {
        var pageSize = ResolvePageSize(limit);

        IQueryable<BackendApi.Modules.Catalog.Entities.Product> query = catalog.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pat = $"%{q.Trim()}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Sku, pat) ||
                EF.Functions.ILike(p.NameAr, pat) ||
                EF.Functions.ILike(p.NameEn, pat));
        }
        if (!string.IsNullOrWhiteSpace(marketCode))
        {
            var mkt = marketCode.Trim().ToLowerInvariant();
            query = query.Where(p => p.MarketCodes.Any(m => m == mkt));
        }

        if (!string.IsNullOrWhiteSpace(cursor) && TryParseCursor(cursor, out var cursorSku, out var cursorId))
        {
            query = query.Where(p =>
                string.Compare(p.Sku, cursorSku) > 0 ||
                (p.Sku == cursorSku && p.Id.CompareTo(cursorId) > 0));
        }

        var rows = await query
            .OrderBy(p => p.Sku)
            .ThenBy(p => p.Id)
            .Take(pageSize + 1)
            .Select(p => new LookupSkuRow(
                p.Sku, p.Id, p.NameAr, p.NameEn, p.Status == "archived"))
            .ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > pageSize)
        {
            var last = rows[pageSize - 1];
            nextCursor = $"{last.Sku}:{last.ProductId:N}";
            rows = rows.Take(pageSize).ToList();
        }

        return Results.Ok(new LookupSkuResponse(rows, nextCursor));
    }

    // -------------------- GET /lookups/companies --------------------

    private static async Task<IResult> SearchCompaniesAsync(
        [FromQuery] string? q,
        [FromQuery(Name = "market_code")] string? marketCode,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        B2BDbContext b2b,
        CancellationToken ct)
    {
        var pageSize = ResolvePageSize(limit);

        IQueryable<BackendApi.Modules.B2B.Entities.Company> query = b2b.Companies.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pat = $"%{q.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.NameJson, pat));
        }
        if (!string.IsNullOrWhiteSpace(marketCode))
        {
            var mkt = marketCode.Trim().ToLowerInvariant();
            query = query.Where(c => c.MarketCode == mkt);
        }

        if (!string.IsNullOrWhiteSpace(cursor) && TryParseGuidCursor(cursor, out var cursorId))
        {
            query = query.Where(c => c.Id.CompareTo(cursorId) > 0);
        }

        var rows = await query
            .OrderBy(c => c.Id)
            .Take(pageSize + 1)
            .Select(c => new LookupCompanyRow(c.Id, c.NameJson, c.MarketCode, c.State))
            .ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > pageSize)
        {
            nextCursor = rows[pageSize - 1].Id.ToString("N");
            rows = rows.Take(pageSize).ToList();
        }

        return Results.Ok(new LookupCompanyResponse(rows, nextCursor));
    }

    // -------------------- GET /lookups/segments --------------------

    /// <summary>
    /// Spec 019 admin-customers segment search is not yet on main. Until it lands,
    /// this endpoint returns an empty page — spec 015's UI surfaces a "no segments
    /// configured" affordance. When 019 ships, this handler will be backed by its
    /// segment-read contract.
    /// </summary>
    private static IResult SearchSegmentsAsync(
        [FromQuery] string? q,
        [FromQuery(Name = "market_code")] string? marketCode,
        [FromQuery] string? cursor,
        [FromQuery] int? limit)
    {
        return Results.Ok(new LookupSegmentResponse(Array.Empty<LookupSegmentRow>(), null));
    }

    // -------------------- helpers --------------------

    private static int ResolvePageSize(int? limit) =>
        limit is null or < 1 ? DefaultPageSize : Math.Min(limit.Value, LookupPageCap);

    private static bool TryParseCursor(string cursor, out string sku, out Guid id)
    {
        sku = string.Empty;
        id = default;
        var parts = cursor.Split(':');
        if (parts.Length != 2) return false;
        if (!Guid.TryParse(parts[1], out id)) return false;
        sku = parts[0];
        return true;
    }

    private static bool TryParseGuidCursor(string cursor, out Guid id) => Guid.TryParse(cursor, out id);
}
