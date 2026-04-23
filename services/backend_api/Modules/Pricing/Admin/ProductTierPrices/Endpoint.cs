using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Admin.ProductTierPrices;

public sealed record UpsertTierPriceRequest(Guid TierId, string MarketCode, long NetMinor);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapProductTierPriceEndpoints(this IEndpointRouteBuilder builder)
    {
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        builder.MapPost("/products/{productId:guid}/tier-prices", UpsertAsync)
            .RequireAuthorization(adminAuth).RequirePermission("pricing.tier.write");
        builder.MapDelete("/products/{productId:guid}/tier-prices", DeleteAsync)
            .RequireAuthorization(adminAuth).RequirePermission("pricing.tier.write");
        return builder;
    }

    private static async Task<IResult> UpsertAsync(
        Guid productId,
        UpsertTierPriceRequest request,
        HttpContext context,
        PricingDbContext db,
        IAuditEventPublisher audit,
        CancellationToken ct)
    {
        var market = request.MarketCode.Trim().ToLowerInvariant();
        if (request.NetMinor < 0)
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.tier_price.invalid", "Net must be >= 0", "");
        }

        var existing = await db.ProductTierPrices
            .SingleOrDefaultAsync(p => p.ProductId == productId && p.TierId == request.TierId && p.MarketCode == market, ct);
        object? before = existing is null ? null : new { existing.NetMinor };
        if (existing is null)
        {
            db.ProductTierPrices.Add(new ProductTierPrice
            {
                ProductId = productId,
                TierId = request.TierId,
                MarketCode = market,
                NetMinor = request.NetMinor,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.NetMinor = request.NetMinor;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin", "pricing.product_tier_price.upserted", nameof(ProductTierPrice), productId,
            before, new { productId, request.TierId, market, request.NetMinor },
            "pricing.product_tier_price.upsert"), ct);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid productId,
        Guid tierId,
        string marketCode,
        HttpContext context,
        PricingDbContext db,
        IAuditEventPublisher audit,
        CancellationToken ct)
    {
        var m = marketCode.Trim().ToLowerInvariant();
        var entity = await db.ProductTierPrices
            .SingleOrDefaultAsync(p => p.ProductId == productId && p.TierId == tierId && p.MarketCode == m, ct);
        if (entity is null)
        {
            return AdminPricingResponseFactory.Problem(context, 404, "pricing.tier_price.not_found", "Not found", "");
        }
        var before = new { entity.NetMinor };
        db.ProductTierPrices.Remove(entity);
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin", "pricing.product_tier_price.deleted", nameof(ProductTierPrice), productId,
            before, null, "pricing.product_tier_price.delete"), ct);

        return Results.NoContent();
    }
}
