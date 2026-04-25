using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Checkout.Customer.Common;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using BackendApi.Modules.Pricing.Primitives;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Checkout.Customer.Summary;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapSummaryEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/sessions/{sessionId:guid}/summary", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid sessionId,
        HttpContext context,
        CheckoutDbContext db,
        CartDbContext cartDb,
        CatalogDbContext catalogDb,
        CartTokenProvider cartTokenProvider,
        IPriceCalculator priceCalculator,
        DriftDetector driftDetector,
        CancellationToken ct)
    {
        var accountId = await CustomerCheckoutResponseFactory.TryResolveAuthenticatedAccountAsync(context);
        var cartToken = StartSession.Endpoint.ResolveCartToken(context);

        var load = await CheckoutSessionLoader.LoadAsync(db, context, sessionId, accountId, cartToken, cartTokenProvider, ct);
        if (load.Problem is not null) return load.Problem;
        var session = load.Session!;

        var pricing = await PricingComputation.RunPreviewAsync(
            cartDb, catalogDb, priceCalculator, session, ct);
        if (pricing.PricingError is not null)
        {
            return Results.Ok(new
            {
                sessionId = session.Id,
                state = session.State,
                pricingError = pricing.PricingError,
            });
        }

        // Stash the preview hash on the session so Submit can compare against it (FR-013).
        var hash = driftDetector.Hash(pricing.Snapshot);
        session.LastPreviewHash = hash;
        session.LastTouchedAt = DateTimeOffset.UtcNow;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            sessionId = session.Id,
            state = session.State,
            expiresAt = session.ExpiresAt,
            shipping = session.ShippingProviderId is null ? null : new
            {
                providerId = session.ShippingProviderId,
                methodCode = session.ShippingMethodCode,
                feeMinor = session.ShippingFeeMinor,
            },
            paymentMethod = session.PaymentMethod,
            pricing = new
            {
                currency = pricing.Snapshot.Currency,
                subtotalMinor = pricing.Snapshot.SubtotalMinor,
                discountMinor = pricing.Snapshot.DiscountMinor,
                taxMinor = pricing.Snapshot.TaxMinor,
                shippingFeeMinor = session.ShippingFeeMinor ?? 0,
                grandTotalMinor = pricing.Snapshot.GrandTotalMinor + (session.ShippingFeeMinor ?? 0),
                lines = pricing.Snapshot.Lines,
            },
        });
    }
}
