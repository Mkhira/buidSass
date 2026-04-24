using BackendApi.Modules.Cart.Customer.Common;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cart.Customer.GetCart;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapGetCartEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        string? market,
        CartDbContext db,
        CatalogDbContext catalogDb,
        BackendApi.Modules.Inventory.Persistence.InventoryDbContext inventoryDb,
        CartResolver resolver,
        CartViewBuilder viewBuilder,
        CustomerContextResolver customerContextResolver,
        CartReservationRehydrator reservationRehydrator,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(market))
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.market_required", "Market required", "The market query parameter is required.");
        }

        var accountId = await CustomerCartResponseFactory.TryResolveAuthenticatedAccountAsync(context);
        var suppliedToken = ResolveToken(context);
        var nowUtc = DateTimeOffset.UtcNow;

        var cart = await resolver.LookupAsync(db, accountId, suppliedToken, market, nowUtc, ct);
        if (cart is null)
        {
            // Return an empty-shape view shaped like CartView so the response schema is stable
            // across "no cart" and "empty cart" cases. No row is created for a GET.
            var normalised = market.Trim().ToLowerInvariant();
            var currency = BackendApi.Modules.Pricing.Primitives.PricingConstants.ResolveCurrency(normalised);
            return Results.Ok(new CartView(
                Id: Guid.Empty,
                MarketCode: normalised,
                Status: CartStatuses.Active,
                Lines: Array.Empty<CartLineView>(),
                SavedItems: Array.Empty<CartSavedItemView>(),
                CouponCode: null,
                Pricing: new CartPricingView(currency, 0, 0, 0, 0, null),
                CheckoutEligibility: new CheckoutEligibilityView(false, "cart.empty"),
                B2b: new CartB2BView(null, null, null, null, null)));
        }

        cart.LastTouchedAt = nowUtc;

        // FR-022 / spec edge case 4: attempt re-reservation for any line whose spec-008
        // reservation has expired or been reaped. Tracked fetch (no AsNoTracking) so the
        // rehydrator can persist ReservationId / StockChanged on the line rows.
        var trackedLines = await db.CartLines.Where(l => l.CartId == cart.Id).OrderBy(l => l.AddedAt).ToListAsync(ct);
        await reservationRehydrator.RehydrateAsync(db, inventoryDb, catalogDb, cart, trackedLines, nowUtc, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (CustomerCartResponseFactory.IsConcurrencyConflict(ex))
        {
            // Another mutation touched the row between our read and the timestamp update —
            // the view below reloads from DB anyway, so just drop the touch and keep going.
            // A GET should never surface 5xx because of a cosmetic timestamp race.
            db.ChangeTracker.Clear();
            cart = await db.Carts.AsNoTracking().SingleAsync(c => c.Id == cart.Id, ct);
        }

        var lines = await db.CartLines.AsNoTracking().Where(l => l.CartId == cart.Id).OrderBy(l => l.AddedAt).ToListAsync(ct);
        var saved = await db.CartSavedItems.AsNoTracking().Where(s => s.CartId == cart.Id).ToListAsync(ct);
        var b2b = await db.CartB2BMetadata.AsNoTracking().SingleOrDefaultAsync(b => b.CartId == cart.Id, ct);

        var ctxInfo = await customerContextResolver.ResolveAsync(accountId, ct);
        var view = await viewBuilder.BuildAsync(
            cart: cart,
            lines: lines,
            savedItems: saved,
            b2bMetadata: b2b,
            catalogDb: catalogDb,
            customerVerifiedForRestriction: ctxInfo.VerifiedForRestriction,
            customerIsB2B: ctxInfo.IsB2B,
            nowUtc: nowUtc,
            cancellationToken: ct);

        return Results.Ok(view);
    }

    internal static string? ResolveToken(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Cart-Token", out var header) && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString();
        }
        if (context.Request.Cookies.TryGetValue("cart_token", out var cookie) && !string.IsNullOrWhiteSpace(cookie))
        {
            return cookie;
        }
        return null;
    }

    internal static void AttachTokenCookie(HttpContext context, string token, int lifetimeDays)
    {
        // Secure flag is gated on the request scheme so the cookie round-trips in local HTTP
        // dev + tests; production traffic is HTTPS so the cookie stays Secure there.
        context.Response.Cookies.Append("cart_token", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(lifetimeDays),
            Path = "/",
        });
        context.Response.Headers["X-Cart-Token"] = token;
    }
}
