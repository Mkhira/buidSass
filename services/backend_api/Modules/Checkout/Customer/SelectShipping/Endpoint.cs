using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Checkout.Customer.Common;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Checkout.Customer.SelectShipping;

public sealed record SelectShippingRequest(string ProviderId, string MethodCode);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapSelectShippingEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPatch("/sessions/{sessionId:guid}/shipping", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid sessionId,
        SelectShippingRequest request,
        HttpContext context,
        CheckoutDbContext db,
        CartTokenProvider cartTokenProvider,
        CheckoutAuditEmitter audit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.ProviderId) || string.IsNullOrWhiteSpace(request?.MethodCode))
        {
            return CustomerCheckoutResponseFactory.Problem(context, 400, "checkout.shipping.invalid", "providerId + methodCode required", "");
        }
        var accountId = await CustomerCheckoutResponseFactory.TryResolveAuthenticatedAccountAsync(context);
        var cartToken = StartSession.Endpoint.ResolveCartToken(context);

        var load = await CheckoutSessionLoader.LoadAsync(db, context, sessionId, accountId, cartToken, cartTokenProvider, ct);
        if (load.Problem is not null) return load.Problem;
        var session = load.Session!;

        if (session.State is not (CheckoutStates.Addressed or CheckoutStates.ShippingSelected or CheckoutStates.PaymentSelected))
        {
            return CustomerCheckoutResponseFactory.Problem(context, 409, "checkout.invalid_state", "Set an address first", "");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var quote = await db.ShippingQuotes
            .AsNoTracking()
            .SingleOrDefaultAsync(q => q.SessionId == session.Id
                && q.ProviderId == request.ProviderId
                && q.MethodCode == request.MethodCode
                && q.ExpiresAt > nowUtc, ct);
        if (quote is null)
        {
            return CustomerCheckoutResponseFactory.Problem(context, 404, "checkout.shipping.quote_expired", "Shipping quote expired or unknown", "Re-request quotes.");
        }

        // CR review on PR #31: snapshot the actual transition intent BEFORE mutating so a
        // re-select (already in ShippingSelected/PaymentSelected) doesn't emit a duplicate
        // transition audit event.
        var transitionedToShippingSelected = session.State == CheckoutStates.Addressed;

        session.ShippingProviderId = quote.ProviderId;
        session.ShippingMethodCode = quote.MethodCode;
        session.ShippingFeeMinor = quote.FeeMinor;
        if (transitionedToShippingSelected)
        {
            CheckoutStates.TryTransition(session, CheckoutStates.ShippingSelected, nowUtc);
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

        // FR-015: audit ONLY the actual Addressed → ShippingSelected transition. Guest =
        // customer (the cart-token holder is still an end user, not the platform).
        if (transitionedToShippingSelected)
        {
            await audit.EmitSessionTransitionAsync(
                session, CheckoutAuditActions.SessionShippingSelected, accountId,
                CheckoutAuditEmitter.CustomerRole,
                reason: $"provider={quote.ProviderId} method={quote.MethodCode}", ct);
        }
        return Results.Ok(new { sessionId = session.Id, state = session.State });
    }
}
