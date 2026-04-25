using System.Text.Json;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Checkout.Customer.Common;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Checkout.Customer.SetAddress;

public sealed record SetAddressRequest(AddressDto Shipping, AddressDto? Billing);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapSetAddressEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPatch("/sessions/{sessionId:guid}/address", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid sessionId,
        SetAddressRequest request,
        HttpContext context,
        CheckoutDbContext db,
        CartTokenProvider cartTokenProvider,
        CheckoutAuditEmitter audit,
        CancellationToken ct)
    {
        if (request is null || request.Shipping is null || !request.Shipping.IsMinimallyValid())
        {
            return CustomerCheckoutResponseFactory.Problem(context, 400, "checkout.address.invalid", "Invalid address", "shipping address fields are required.");
        }
        var accountId = await CustomerCheckoutResponseFactory.TryResolveAuthenticatedAccountAsync(context);
        var cartToken = StartSession.Endpoint.ResolveCartToken(context);

        var load = await CheckoutSessionLoader.LoadAsync(db, context, sessionId, accountId, cartToken, cartTokenProvider, ct);
        if (load.Problem is not null) return load.Problem;
        var session = load.Session!;

        // Drop shipping quote selections + any cached quotes — address change invalidates them (US5.3).
        var addressChanged = session.ShippingAddressJson is null
            || !JsonDocument.Parse(session.ShippingAddressJson).RootElement.ToString()
                .Equals(JsonSerializer.Serialize(request.Shipping), StringComparison.Ordinal);

        // CR review on PR #31: capture transition intent BEFORE mutating state so the audit
        // emit doesn't fire on no-op re-saves while already in `Addressed`. Two real-transition
        // shapes: (a) first entry from Init, (b) re-entry triggered by an address change while
        // already past Addressed (we walk back to Addressed in the block below).
        var enteredAddressedFromInit = session.State == CheckoutStates.Init;
        var reenteredAddressed = addressChanged
            && session.State is CheckoutStates.ShippingSelected or CheckoutStates.PaymentSelected;

        if (addressChanged)
        {
            session.ShippingProviderId = null;
            session.ShippingMethodCode = null;
            session.ShippingFeeMinor = null;
            await db.ShippingQuotes.Where(q => q.SessionId == session.Id).ExecuteDeleteAsync(ct);
            // Reset state to `addressed` even if we were at a later step.
            if (session.State is CheckoutStates.ShippingSelected or CheckoutStates.PaymentSelected)
            {
                CheckoutStates.TryTransition(session, CheckoutStates.Addressed, DateTimeOffset.UtcNow);
            }
        }

        session.ShippingAddressJson = JsonSerializer.Serialize(request.Shipping);
        session.BillingAddressJson = JsonSerializer.Serialize(request.Billing ?? request.Shipping);
        var nowUtc = DateTimeOffset.UtcNow;
        if (session.State == CheckoutStates.Init)
        {
            CheckoutStates.TryTransition(session, CheckoutStates.Addressed, nowUtc);
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

        // FR-015: audit ONLY on a real transition into Addressed; guest = customer.
        if (enteredAddressedFromInit || reenteredAddressed)
        {
            await audit.EmitSessionTransitionAsync(
                session, CheckoutAuditActions.SessionAddressed, accountId,
                CheckoutAuditEmitter.CustomerRole,
                reason: enteredAddressedFromInit ? "address_set" : "address_changed", ct);
        }
        return Results.Ok(new { sessionId = session.Id, state = session.State, expiresAt = session.ExpiresAt });
    }
}
