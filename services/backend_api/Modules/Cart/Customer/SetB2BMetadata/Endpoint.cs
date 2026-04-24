using BackendApi.Modules.Cart.Customer.Common;
using BackendApi.Modules.Cart.Entities;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Pricing.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cart.Customer.SetB2BMetadata;

public sealed record SetB2BMetadataRequest(
    string MarketCode,
    string? PoNumber,
    string? Reference,
    string? Notes,
    DateTimeOffset? RequestedDeliveryFrom,
    DateTimeOffset? RequestedDeliveryTo);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapSetB2BMetadataEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPut("/b2b", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        SetB2BMetadataRequest request,
        HttpContext context,
        CartDbContext db,
        CatalogDbContext catalogDb,
        PricingDbContext pricingDb,
        CartResolver resolver,
        CartViewBuilder viewBuilder,
        CustomerContextResolver customerContextResolver,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.MarketCode))
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.market_required", "Market required", "");
        }
        // Normalise market early so all downstream lookups (cart resolve + b2b tier + response)
        // operate on the same canonical form.
        var normalizedMarket = request.MarketCode.Trim().ToLowerInvariant();
        var accountId = CustomerCartResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return CustomerCartResponseFactory.Problem(context, 401, "cart.auth_required", "Auth required", "");
        }

        // B2B gate: account must have a pricing.account_b2b_tiers row (Principle 9). Note that
        // the B2BTier + AccountB2BTier model does not yet carry a per-market column; per-market
        // B2B scoping is tracked for Phase 1.5 (spec 011). Today, an account is either a B2B
        // tier holder globally or not. This endpoint therefore checks AccountId only — adding a
        // false market filter would reject every legitimate call.
        var isB2B = await pricingDb.AccountB2BTiers.AsNoTracking().AnyAsync(t => t.AccountId == accountId.Value, ct);
        if (!isB2B)
        {
            return CustomerCartResponseFactory.Problem(
                context, 403, "cart.b2b_fields_forbidden", "B2B required",
                "B2B metadata can only be set by accounts with a B2B tier assignment.");
        }

        if (request.RequestedDeliveryFrom is { } from && request.RequestedDeliveryTo is { } to && from > to)
        {
            return CustomerCartResponseFactory.Problem(
                context, 400, "cart.b2b_invalid_delivery_window",
                "Invalid delivery window", "requestedDeliveryFrom must be before requestedDeliveryTo.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var resolved = await resolver.ResolveOrCreateAsync(db, accountId, suppliedToken: null, normalizedMarket, nowUtc, ct);
        var cart = resolved.Cart;
        await db.SaveChangesAsync(ct);

        var metadata = await db.CartB2BMetadata.SingleOrDefaultAsync(m => m.CartId == cart.Id, ct);
        if (metadata is null)
        {
            metadata = new CartB2BMetadata { CartId = cart.Id, MarketCode = cart.MarketCode };
            db.CartB2BMetadata.Add(metadata);
        }
        metadata.PoNumber = request.PoNumber;
        metadata.Reference = request.Reference;
        metadata.Notes = request.Notes;
        metadata.RequestedDeliveryFrom = request.RequestedDeliveryFrom;
        metadata.RequestedDeliveryTo = request.RequestedDeliveryTo;
        metadata.UpdatedAt = nowUtc;
        cart.LastTouchedAt = nowUtc;
        cart.UpdatedAt = nowUtc;
        await db.SaveChangesAsync(ct);

        var lines = await db.CartLines.AsNoTracking().Where(l => l.CartId == cart.Id).OrderBy(l => l.AddedAt).ToListAsync(ct);
        var saved = await db.CartSavedItems.AsNoTracking().Where(s => s.CartId == cart.Id).ToListAsync(ct);
        var ctxInfo = await customerContextResolver.ResolveAsync(accountId, ct);
        var view = await viewBuilder.BuildAsync(cart, lines, saved, metadata, catalogDb, ctxInfo.VerifiedForRestriction, ctxInfo.IsB2B, nowUtc, ct);
        return Results.Ok(view);
    }
}
