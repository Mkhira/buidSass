using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Checkout.Customer.Common;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using BackendApi.Modules.Pricing.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Checkout.Customer.SelectPaymentMethod;

public sealed record SelectPaymentMethodRequest(string Method);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapSelectPaymentMethodEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPatch("/sessions/{sessionId:guid}/payment-method", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid sessionId,
        SelectPaymentMethodRequest request,
        HttpContext context,
        CheckoutDbContext db,
        CartDbContext cartDb,
        CartTokenProvider cartTokenProvider,
        PaymentMethodCatalog methodCatalog,
        PricingDbContext pricingDb,
        CatalogDbContext catalogDb,
        CheckoutAuditEmitter audit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Method))
        {
            return CustomerCheckoutResponseFactory.Problem(context, 400, "checkout.payment.method_required", "method required", "");
        }
        var method = request.Method.Trim().ToLowerInvariant();
        var accountId = await CustomerCheckoutResponseFactory.TryResolveAuthenticatedAccountAsync(context);
        var cartToken = StartSession.Endpoint.ResolveCartToken(context);

        var load = await CheckoutSessionLoader.LoadAsync(db, context, sessionId, accountId, cartToken, cartTokenProvider, ct);
        if (load.Problem is not null) return load.Problem;
        var session = load.Session!;

        if (session.State is not (CheckoutStates.ShippingSelected or CheckoutStates.PaymentSelected))
        {
            return CustomerCheckoutResponseFactory.Problem(context, 409, "checkout.invalid_state", "Select shipping first", "");
        }

        if (!methodCatalog.IsMethodAllowed(session.MarketCode, method))
        {
            return CustomerCheckoutResponseFactory.Problem(
                context, 400, "checkout.payment.method_not_supported",
                "Method not supported in market", $"Method {method} is not configured for {session.MarketCode}.");
        }

        // B2B bank transfer → PO required (US4.3 / FR-020). PO lives on the cart's b2b metadata.
        if (string.Equals(method, PaymentMethodCatalog.BankTransfer, StringComparison.OrdinalIgnoreCase))
        {
            var isB2B = accountId is { } aid
                && await pricingDb.AccountB2BTiers.AsNoTracking().AnyAsync(t => t.AccountId == aid, ct);
            if (!isB2B)
            {
                return CustomerCheckoutResponseFactory.Problem(context, 403, "checkout.b2b_required", "Bank transfer requires a B2B account", "");
            }
            var b2b = await cartDb.CartB2BMetadata.AsNoTracking().SingleOrDefaultAsync(m => m.CartId == session.CartId, ct);
            if (b2b is null || string.IsNullOrWhiteSpace(b2b.PoNumber))
            {
                return CustomerCheckoutResponseFactory.Problem(context, 400, "checkout.b2b.po_required", "PO number required", "Set PO on the cart before selecting bank_transfer.");
            }
        }

        // COD cap + restriction check (US6 / FR-011). Requires the cart's current subtotal,
        // which for this endpoint we approximate via cart lines × product price hints — the
        // Submit handler runs the authoritative check via pricing Issue mode.
        if (string.Equals(method, PaymentMethodCatalog.Cod, StringComparison.OrdinalIgnoreCase))
        {
            var hasRestricted = await cartDb.CartLines.AsNoTracking()
                .AnyAsync(l => l.CartId == session.CartId && l.Restricted, ct);
            var totalMinor = session.ShippingFeeMinor ?? 0;
            totalMinor += await EstimateCartTotalAsync(cartDb, catalogDb, session.CartId, ct);
            var eligibility = methodCatalog.CheckCod(session.MarketCode, totalMinor, hasRestricted);
            if (!eligibility.Allowed)
            {
                return CustomerCheckoutResponseFactory.Problem(context, 400, eligibility.ReasonCode!, "COD not eligible", "");
            }
        }

        session.PaymentMethod = method;
        var nowUtc = DateTimeOffset.UtcNow;
        if (session.State == CheckoutStates.ShippingSelected)
        {
            CheckoutStates.TryTransition(session, CheckoutStates.PaymentSelected, nowUtc);
        }
        else
        {
            session.LastTouchedAt = nowUtc;
            session.UpdatedAt = nowUtc;
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (CustomerCheckoutResponseFactory.IsConcurrencyConflict(ex))
        {
            return CustomerCheckoutResponseFactory.Problem(context, 409, "checkout.concurrency_conflict", "Concurrency conflict", "Retry.");
        }

        // FR-015: audit payment_selected (only on the actual transition).
        if (session.State == CheckoutStates.PaymentSelected)
        {
            await audit.EmitSessionTransitionAsync(
                session, CheckoutAuditActions.SessionPaymentSelected, accountId,
                accountId is null ? CheckoutAuditEmitter.SystemRole : CheckoutAuditEmitter.CustomerRole,
                reason: $"method={method}", ct);
        }
        return Results.Ok(new { sessionId = session.Id, state = session.State, paymentMethod = session.PaymentMethod });
    }

    private static async Task<long> EstimateCartTotalAsync(CartDbContext cartDb, CatalogDbContext catalogDb, Guid cartId, CancellationToken ct)
    {
        // Sum PriceHintMinorUnits × Qty for a rough pre-submit COD gate. Submit runs the
        // authoritative pricing Issue which includes tax + coupon + promo layers; this gate
        // only needs to be in the right order of magnitude.
        var lines = await cartDb.CartLines.AsNoTracking()
            .Where(l => l.CartId == cartId)
            .Select(l => new { l.ProductId, l.Qty })
            .ToListAsync(ct);
        if (lines.Count == 0) return 0L;
        var ids = lines.Select(l => l.ProductId).ToArray();
        var prices = await catalogDb.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.PriceHintMinorUnits })
            .ToDictionaryAsync(p => p.Id, p => p.PriceHintMinorUnits ?? 0L, ct);
        return lines.Sum(l => (long)l.Qty * prices.GetValueOrDefault(l.ProductId, 0L));
    }
}
