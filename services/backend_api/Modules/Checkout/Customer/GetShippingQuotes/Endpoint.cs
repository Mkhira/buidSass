using System.Text.Json;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Checkout.Customer.Common;
using BackendApi.Modules.Checkout.Entities;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using BackendApi.Modules.Checkout.Primitives.Shipping;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Checkout.Customer.GetShippingQuotes;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapGetShippingQuotesEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/sessions/{sessionId:guid}/shipping-quotes", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid sessionId,
        HttpContext context,
        CheckoutDbContext db,
        CartTokenProvider cartTokenProvider,
        IShippingProvider shippingProvider,
        IOptions<CheckoutOptions> options,
        CancellationToken ct)
    {
        var accountId = await CustomerCheckoutResponseFactory.TryResolveAuthenticatedAccountAsync(context);
        var cartToken = StartSession.Endpoint.ResolveCartToken(context);

        var load = await CheckoutSessionLoader.LoadAsync(db, context, sessionId, accountId, cartToken, cartTokenProvider, ct);
        if (load.Problem is not null) return load.Problem;
        var session = load.Session!;

        if (string.IsNullOrWhiteSpace(session.ShippingAddressJson))
        {
            return CustomerCheckoutResponseFactory.Problem(context, 400, "checkout.address.missing", "Address required", "Set a shipping address first.");
        }
        if (!shippingProvider.Supports(session.MarketCode))
        {
            return CustomerCheckoutResponseFactory.Problem(context, 400, "checkout.address_unserviceable", "No provider for market", "");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var existing = await db.ShippingQuotes.AsNoTracking()
            .Where(q => q.SessionId == session.Id && q.ExpiresAt > nowUtc)
            .OrderBy(q => q.FeeMinor)
            .ToListAsync(ct);
        if (existing.Count == 0)
        {
            var shipping = JsonSerializer.Deserialize<AddressDto>(session.ShippingAddressJson!);
            if (shipping is null)
            {
                return CustomerCheckoutResponseFactory.Problem(context, 400, "checkout.address.invalid", "Invalid address", "");
            }
            var providerAddress = new ShippingAddress(
                shipping.FullName, shipping.PhoneE164, shipping.Line1, shipping.Line2,
                shipping.City, shipping.Region, shipping.PostalCode, shipping.CountryCode);
            var quoteReq = new QuoteRequest(session.MarketCode, providerAddress, PackageWeightKg: 1m, DeclaredValueMinor: 0, Currency: "SAR");
            var offers = await shippingProvider.QuoteAsync(quoteReq, ct);
            if (offers.Count == 0)
            {
                return CustomerCheckoutResponseFactory.Problem(context, 400, "checkout.address_unserviceable", "No quotes available", "");
            }
            var ttl = TimeSpan.FromMinutes(options.Value.ShippingQuoteTtlMinutes);
            foreach (var offer in offers)
            {
                db.ShippingQuotes.Add(new ShippingQuote
                {
                    Id = Guid.NewGuid(),
                    SessionId = session.Id,
                    ProviderId = shippingProvider.ProviderId,
                    MethodCode = offer.MethodCode,
                    EtaMinDays = offer.EtaMinDays,
                    EtaMaxDays = offer.EtaMaxDays,
                    FeeMinor = offer.FeeMinor,
                    Currency = offer.Currency,
                    ExpiresAt = nowUtc.Add(ttl),
                    PayloadJson = offer.PayloadJson,
                    CreatedAt = nowUtc,
                });
            }
            session.LastTouchedAt = nowUtc;
            session.UpdatedAt = nowUtc;
            await db.SaveChangesAsync(ct);
            existing = await db.ShippingQuotes.AsNoTracking()
                .Where(q => q.SessionId == session.Id && q.ExpiresAt > nowUtc)
                .OrderBy(q => q.FeeMinor).ToListAsync(ct);
        }

        return Results.Ok(new
        {
            quotes = existing.Select(q => new
            {
                providerId = q.ProviderId,
                methodCode = q.MethodCode,
                etaMinDays = q.EtaMinDays,
                etaMaxDays = q.EtaMaxDays,
                feeMinor = q.FeeMinor,
                currency = q.Currency,
                expiresAt = q.ExpiresAt,
            }).ToArray(),
        });
    }
}
