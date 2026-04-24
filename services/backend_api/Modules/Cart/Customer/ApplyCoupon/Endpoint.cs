using BackendApi.Modules.Cart.Customer.Common;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Pricing.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cart.Customer.ApplyCoupon;

public sealed record ApplyCouponRequest(string MarketCode, string Code);
public sealed record RemoveCouponRequest(string MarketCode);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapCouponEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/coupon", ApplyAsync);
        builder.MapDelete("/coupon", RemoveAsync);
        return builder;
    }

    private static async Task<IResult> ApplyAsync(
        ApplyCouponRequest request,
        HttpContext context,
        CartDbContext db,
        CatalogDbContext catalogDb,
        PricingDbContext pricingDb,
        CartResolver resolver,
        CartViewBuilder viewBuilder,
        CustomerContextResolver customerContextResolver,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.coupon.invalid", "Code required", "");
        }
        if (string.IsNullOrWhiteSpace(request.MarketCode))
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.market_required", "Market required", "");
        }
        var nowUtc = DateTimeOffset.UtcNow;
        var accountId = await CustomerCartResponseFactory.TryResolveAuthenticatedAccountAsync(context);
        var suppliedToken = GetCart.Endpoint.ResolveToken(context);
        var marketCode = request.MarketCode.Trim().ToLowerInvariant();

        var cart = await resolver.LookupAsync(db, accountId, suppliedToken, marketCode, nowUtc, ct);
        if (cart is null)
        {
            return CustomerCartResponseFactory.Problem(context, 404, "cart.not_found", "Cart not found", "");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        var coupon = await pricingDb.Coupons.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == code && c.DeletedAt == null, ct);

        if (coupon is null || !coupon.IsActive)
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.coupon.invalid", "Invalid coupon", "Coupon not found or inactive.");
        }
        if (coupon.ValidFrom is { } vf && nowUtc < vf)
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.coupon.expired", "Coupon not yet valid", "");
        }
        if (coupon.ValidTo is { } vt && nowUtc > vt)
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.coupon.expired", "Coupon has expired", "");
        }
        if (coupon.MarketCodes.Length > 0 && !coupon.MarketCodes.Any(m => string.Equals(m, marketCode, StringComparison.OrdinalIgnoreCase)))
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.coupon.invalid", "Coupon not valid in this market", "");
        }
        if (coupon.OverallLimit is { } limit && coupon.UsedCount >= limit)
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.coupon.limit_reached", "Coupon redemption limit reached", "");
        }
        if (coupon.ExcludesRestricted)
        {
            var hasRestricted = await db.CartLines
                .AsNoTracking()
                .Where(l => l.CartId == cart.Id)
                .AnyAsync(l => l.Restricted, ct);
            if (hasRestricted)
            {
                return CustomerCartResponseFactory.Problem(
                    context, 400, "cart.coupon.excludes_restricted",
                    "Coupon cannot apply to restricted products", "");
            }
        }

        cart.CouponCode = code;
        cart.LastTouchedAt = nowUtc;
        cart.UpdatedAt = nowUtc;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return CustomerCartResponseFactory.ConcurrencyConflict(context, "Cart was modified by another request.");
        }

        return Results.Ok(await BuildAsync(db, catalogDb, viewBuilder, customerContextResolver, cart, accountId, nowUtc, ct));
    }

    private static async Task<IResult> RemoveAsync(
        string? market,
        HttpContext context,
        CartDbContext db,
        CatalogDbContext catalogDb,
        CartResolver resolver,
        CartViewBuilder viewBuilder,
        CustomerContextResolver customerContextResolver,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(market))
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.market_required", "Market required", "");
        }
        var normalizedMarket = market.Trim().ToLowerInvariant();
        var nowUtc = DateTimeOffset.UtcNow;
        var accountId = await CustomerCartResponseFactory.TryResolveAuthenticatedAccountAsync(context);
        var suppliedToken = GetCart.Endpoint.ResolveToken(context);
        var cart = await resolver.LookupAsync(db, accountId, suppliedToken, normalizedMarket, nowUtc, ct);
        if (cart is null)
        {
            return CustomerCartResponseFactory.Problem(context, 404, "cart.not_found", "Cart not found", "");
        }
        cart.CouponCode = null;
        cart.LastTouchedAt = nowUtc;
        cart.UpdatedAt = nowUtc;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return CustomerCartResponseFactory.ConcurrencyConflict(context, "Cart was modified by another request.");
        }
        return Results.Ok(await BuildAsync(db, catalogDb, viewBuilder, customerContextResolver, cart, accountId, nowUtc, ct));
    }

    private static async Task<CartView> BuildAsync(
        CartDbContext db,
        CatalogDbContext catalogDb,
        CartViewBuilder vb,
        CustomerContextResolver customerContextResolver,
        Entities.Cart cart,
        Guid? accountId,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var ctxInfo = await customerContextResolver.ResolveAsync(accountId, ct);
        var lines = await db.CartLines.AsNoTracking().Where(l => l.CartId == cart.Id).OrderBy(l => l.AddedAt).ToListAsync(ct);
        var saved = await db.CartSavedItems.AsNoTracking().Where(s => s.CartId == cart.Id).ToListAsync(ct);
        var b2b = await db.CartB2BMetadata.AsNoTracking().SingleOrDefaultAsync(b => b.CartId == cart.Id, ct);
        return await vb.BuildAsync(cart, lines, saved, b2b, catalogDb, ctxInfo.VerifiedForRestriction, ctxInfo.IsB2B, nowUtc, ct);
    }
}
