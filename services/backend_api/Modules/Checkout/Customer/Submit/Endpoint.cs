using System.Text.Json;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Checkout.Customer.Common;
using BackendApi.Modules.Checkout.Entities;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using BackendApi.Modules.Checkout.Primitives.Payment;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Observability;
using BackendApi.Modules.Pricing.Primitives;
using BackendApi.Modules.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Checkout.Customer.Submit;

public sealed record SubmitRequest(
    string? ProviderToken,
    long? AcceptedTotalMinor);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapSubmitEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/sessions/{sessionId:guid}/submit", HandleAsync)
            // FR-019: submit requires auth — unauthenticated callers get 401 checkout.requires_auth.
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid sessionId,
        SubmitRequest? request,
        HttpContext context,
        CheckoutDbContext db,
        CartDbContext cartDb,
        CatalogDbContext catalogDb,
        InventoryDbContext inventoryDb,
        CartTokenProvider cartTokenProvider,
        IPaymentGateway paymentGateway,
        IPriceCalculator priceCalculator,
        DriftDetector driftDetector,
        IdempotencyStore idempotencyStore,
        IOrderFromCheckoutHandler orderHandler,
        CheckoutMetrics metrics,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var accountId = CustomerCheckoutResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return CustomerCheckoutResponseFactory.Problem(context, 401, "checkout.requires_auth", "Auth required", "",
                new Dictionary<string, object?> { ["nextStep"] = "login" });
        }

        // FR-007 — idempotency key mandatory on submit.
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader)
            || string.IsNullOrWhiteSpace(idempotencyHeader))
        {
            return CustomerCheckoutResponseFactory.Problem(context, 400, "checkout.idempotency_required", "Idempotency-Key required", "");
        }
        var idempotencyKey = idempotencyHeader.ToString();
        var normalizedBody = JsonSerializer.Serialize(new
        {
            sessionId,
            providerToken = request?.ProviderToken ?? "",
            accepted = request?.AcceptedTotalMinor ?? 0,
        });

        // Atomic claim — see IdempotencyStore docstring. Two concurrent first-use submits
        // with the same key only have one "Claimed" winner; the rest see InProgress / Hit /
        // BodyMismatch deterministically.
        var claim = await idempotencyStore.TryClaimAsync(idempotencyKey, accountId, normalizedBody, ct);
        if (claim.Outcome == IdempotencyStore.ClaimOutcome.Hit)
        {
            metrics.IncrementOutcome("unknown", "idempotent_hit");
            metrics.RecordSubmitDuration(stopwatch.Elapsed.TotalMilliseconds, "unknown", "idempotent_hit");
            return Results.Json(
                JsonSerializer.Deserialize<JsonElement>(claim.Cached!.ResponseJson),
                statusCode: claim.Cached.ResponseStatus);
        }
        if (claim.Outcome == IdempotencyStore.ClaimOutcome.KeyReuseWithDifferentBody)
        {
            return CustomerCheckoutResponseFactory.Problem(context, 422, "checkout.idempotency_body_mismatch",
                "Idempotency-Key reused with a different body", "");
        }
        if (claim.Outcome == IdempotencyStore.ClaimOutcome.InProgress)
        {
            return CustomerCheckoutResponseFactory.Problem(context, 409, "checkout.in_progress",
                "Submit already in flight for this idempotency key", "Retry shortly.");
        }

        // We own the claim. Any exit that doesn't call PersistAsync MUST release the
        // placeholder so a subsequent retry isn't blocked by a stale 5-min TTL.
        var claimFinalized = false;
        try
        {
        var load = await CheckoutSessionLoader.LoadAsync(db, context, sessionId, accountId, suppliedCartToken: null, cartTokenProvider, ct);
        if (load.Problem is not null) return load.Problem;
        var session = load.Session!;

        if (session.State != CheckoutStates.PaymentSelected)
        {
            if (session.State == CheckoutStates.Submitted || session.State == CheckoutStates.Confirmed)
            {
                return CustomerCheckoutResponseFactory.Problem(context, 409, "checkout.already_submitted",
                    "Session already submitted",
                    "",
                    new Dictionary<string, object?> { ["orderId"] = session.OrderId });
            }
            return CustomerCheckoutResponseFactory.Problem(context, 409, "checkout.invalid_state",
                "Complete address + shipping + payment-method selection first", "");
        }
        if (session.AccountId is null)
        {
            session.AccountId = accountId;
            session.CartTokenHash = null;
        }

        // FR-009: re-check restriction eligibility at submit. Unverified customers can't check out
        // with a restricted line (spec 010 US3, SC-005).
        var hasRestricted = await cartDb.CartLines.AsNoTracking()
            .AnyAsync(l => l.CartId == session.CartId && l.Restricted, ct);
        if (hasRestricted)
        {
            var verificationStatus = (await db.Database
                .SqlQuery<string>($"SELECT \"ProfessionalVerificationStatus\" FROM identity.accounts WHERE \"Id\" = {accountId}")
                .ToListAsync(ct)).FirstOrDefault();
            if (!string.Equals(verificationStatus, "verified", StringComparison.OrdinalIgnoreCase))
            {
                return CustomerCheckoutResponseFactory.Problem(context, 403, "checkout.restricted_not_allowed",
                    "Restricted product requires verified account",
                    "catalog.restricted.verification_required");
            }
        }

        // Pre-allocate the orderId so Pricing Issue can bind its explanation row to it, and
        // spec 011's order handler uses the same id when it creates the row below.
        var preallocatedOrderId = Guid.NewGuid();

        // Authoritative pricing via Issue mode (FR-008 step 2).
        var pricing = await PricingComputation.RunIssueAsync(cartDb, catalogDb, priceCalculator, session, preallocatedOrderId, ct);
        if (pricing.PricingError is not null)
        {
            return CustomerCheckoutResponseFactory.Problem(context, 500, "checkout.pricing_failed",
                "Pricing could not be issued", pricing.PricingError);
        }

        // FR-013 / R4: drift check.
        var currentHash = driftDetector.Hash(pricing.Snapshot);
        if (driftDetector.HasDrifted(session.LastPreviewHash, currentHash))
        {
            if (session.AcceptedDriftAt is null
                || session.LastPreviewHash is null
                || (request?.AcceptedTotalMinor is { } acc && acc != pricing.Snapshot.GrandTotalMinor + (session.ShippingFeeMinor ?? 0)))
            {
                metrics.IncrementDrift(session.MarketCode);
                metrics.IncrementOutcome(session.MarketCode, "drift");
                metrics.RecordSubmitDuration(stopwatch.Elapsed.TotalMilliseconds, session.MarketCode, "drift");
                return CustomerCheckoutResponseFactory.Problem(context, 409, "checkout.pricing_drift",
                    "Pricing changed since last review",
                    "Confirm the new total via accept-drift before re-submit.",
                    new Dictionary<string, object?>
                    {
                        ["newGrandTotalMinor"] = pricing.Snapshot.GrandTotalMinor + (session.ShippingFeeMinor ?? 0),
                        ["currency"] = pricing.Snapshot.Currency,
                    });
            }
        }

        // FR-008 step 3: verify reservations are still live.
        var expectedReservationIds = pricing.PerLine.Where(p => p.ReservationId.HasValue).Select(p => p.ReservationId!.Value).ToArray();
        var liveReservationCount = await inventoryDb.InventoryReservations.AsNoTracking()
            .CountAsync(r => expectedReservationIds.Contains(r.Id) && r.Status == "active", ct);
        if (liveReservationCount != expectedReservationIds.Length)
        {
            return CustomerCheckoutResponseFactory.Problem(context, 409, "checkout.inventory_lost",
                "Inventory reservations expired",
                "Some reserved stock has been released; start a fresh cart.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var grandTotalMinor = pricing.Snapshot.GrandTotalMinor + (session.ShippingFeeMinor ?? 0);
        var currency = pricing.Snapshot.Currency;
        var method = session.PaymentMethod!;

        // Record a PaymentAttempt row BEFORE the gateway call so we have a trail even if the
        // gateway hangs or we crash mid-authorize.
        var attempt = new PaymentAttempt
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            ProviderId = paymentGateway.ProviderId,
            Method = method,
            AmountMinor = grandTotalMinor,
            Currency = currency,
            State = PaymentAttemptStates.Initiated,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        db.PaymentAttempts.Add(attempt);

        // Move session to submitted before the gateway call (FR-024: session immutable after).
        CheckoutStates.TryTransition(session, CheckoutStates.Submitted, nowUtc);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (CustomerCheckoutResponseFactory.IsConcurrencyConflict(ex))
        {
            return CustomerCheckoutResponseFactory.Problem(context, 409, "checkout.concurrency_conflict", "Concurrent submit", "");
        }

        // Bank transfer + COD skip the gateway — the order is created in pending state.
        var isAsyncMethod = string.Equals(method, PaymentMethodCatalog.BankTransfer, StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, PaymentMethodCatalog.Cod, StringComparison.OrdinalIgnoreCase);

        string providerTxnId = string.Empty;
        if (!isAsyncMethod)
        {
            var authorize = await paymentGateway.AuthorizeAsync(
                new AuthorizeRequest(session.Id, method, grandTotalMinor, currency, request?.ProviderToken),
                ct);
            if (!authorize.IsSuccess || authorize.Kind == AuthorizeResultKind.Declined || authorize.Kind == AuthorizeResultKind.Failed)
            {
                attempt.State = authorize.Kind == AuthorizeResultKind.Declined ? PaymentAttemptStates.Declined : PaymentAttemptStates.Failed;
                attempt.ErrorCode = authorize.ErrorCode;
                attempt.ErrorMessage = authorize.ErrorMessage;
                attempt.UpdatedAt = DateTimeOffset.UtcNow;
                // FR-022: payment failure returns session to payment_selected; reservations intact.
                CheckoutStates.TryTransition(session, CheckoutStates.Failed, DateTimeOffset.UtcNow);
                CheckoutStates.TryTransition(session, CheckoutStates.PaymentSelected, DateTimeOffset.UtcNow);
                session.FailureReasonCode = "checkout.payment.declined";
                await db.SaveChangesAsync(ct);
                metrics.IncrementOutcome(session.MarketCode, "declined");
                metrics.RecordSubmitDuration(stopwatch.Elapsed.TotalMilliseconds, session.MarketCode, "declined");
                return CustomerCheckoutResponseFactory.Problem(context, 402, "checkout.payment.declined",
                    "Payment declined", authorize.ErrorMessage ?? "Try a different payment method.");
            }
            providerTxnId = authorize.ProviderTxnId;
            attempt.ProviderTxnId = providerTxnId;
            attempt.State = authorize.Kind == AuthorizeResultKind.CapturedSynchronously
                ? PaymentAttemptStates.Captured
                : authorize.Kind == AuthorizeResultKind.Authorized
                    ? PaymentAttemptStates.Authorized
                    : PaymentAttemptStates.PendingWebhook;
        }
        else
        {
            attempt.State = PaymentAttemptStates.PendingWebhook;
        }

        // Hand off to orders (spec 011 — today a stub). On failure we must compensate the
        // payment authorization (R12 / spec edge case 9).
        var orderRequest = new OrderFromCheckoutRequest(
            PreallocatedOrderId: preallocatedOrderId,
            SessionId: session.Id,
            CartId: session.CartId,
            AccountId: accountId.Value,
            MarketCode: session.MarketCode,
            Lines: pricing.PerLine.Select(p => new OrderFromCheckoutLine(
                p.ProductId, p.Qty, p.ListMinor / Math.Max(1, p.Qty), p.NetMinor, p.TaxMinor, p.GrossMinor, p.ReservationId)).ToArray(),
            CouponCode: session.CouponCode,
            PaymentMethod: method,
            PaymentProviderId: paymentGateway.ProviderId,
            PaymentProviderTxnId: string.IsNullOrEmpty(providerTxnId) ? null : providerTxnId,
            ShippingFeeMinor: session.ShippingFeeMinor ?? 0,
            ShippingProviderId: session.ShippingProviderId ?? "",
            ShippingMethodCode: session.ShippingMethodCode ?? "",
            ShippingAddressJson: session.ShippingAddressJson ?? "{}",
            BillingAddressJson: session.BillingAddressJson ?? session.ShippingAddressJson ?? "{}",
            SubtotalMinor: pricing.Snapshot.SubtotalMinor,
            DiscountMinor: pricing.Snapshot.DiscountMinor,
            TaxMinor: pricing.Snapshot.TaxMinor,
            GrandTotalMinor: grandTotalMinor,
            Currency: currency,
            IssuedExplanationId: pricing.ExplanationId ?? Guid.Empty);

        var orderResult = await orderHandler.CreateAsync(orderRequest, ct);
        if (!orderResult.IsSuccess)
        {
            // Compensation: void the authorization if we captured synchronously.
            if (!isAsyncMethod && !string.IsNullOrEmpty(providerTxnId)
                && Guid.TryParse(providerTxnId, out var voidable))
            {
                try { await paymentGateway.VoidAsync(voidable, "checkout.order_create_failed", ct); }
                catch { /* best-effort */ }
                attempt.State = PaymentAttemptStates.Voided;
            }
            CheckoutStates.TryTransition(session, CheckoutStates.Failed, DateTimeOffset.UtcNow);
            session.FailureReasonCode = orderResult.ErrorCode ?? "checkout.order_create_failed";
            await db.SaveChangesAsync(ct);
            loggerFactory.CreateLogger("Checkout.Submit").LogError(
                "checkout.submit.order_create_failed sessionId={SessionId} reason={Reason}",
                session.Id, orderResult.ErrorCode);
            metrics.IncrementOutcome(session.MarketCode, "failed");
            metrics.RecordSubmitDuration(stopwatch.Elapsed.TotalMilliseconds, session.MarketCode, "failed");
            return CustomerCheckoutResponseFactory.Problem(context, 500, "checkout.order_create_failed",
                "Order creation failed", orderResult.ErrorMessage ?? "Operator will refund any captured payment.");
        }

        session.OrderId = orderResult.OrderId;
        session.IssuedExplanationId = pricing.ExplanationId;
        CheckoutStates.TryTransition(session, CheckoutStates.Confirmed, DateTimeOffset.UtcNow);
        attempt.UpdatedAt = DateTimeOffset.UtcNow;

        // Consume the cart (FR-023) — transition to `merged` so a fresh cart is created lazily.
        var cart = await cartDb.Carts.SingleAsync(c => c.Id == session.CartId, ct);
        BackendApi.Modules.Cart.Primitives.CartStatuses.TryTransition(cart, BackendApi.Modules.Cart.Primitives.CartStatuses.Merged, "checkout.submitted", DateTimeOffset.UtcNow);

        var response = new
        {
            orderId = orderResult.OrderId,
            orderNumber = orderResult.OrderNumber,
            paymentState = orderResult.PaymentState,
            invoicePending = true,
            pricing = new
            {
                currency,
                subtotalMinor = pricing.Snapshot.SubtotalMinor,
                discountMinor = pricing.Snapshot.DiscountMinor,
                taxMinor = pricing.Snapshot.TaxMinor,
                shippingFeeMinor = session.ShippingFeeMinor ?? 0,
                grandTotalMinor,
            },
            shipping = new
            {
                providerId = session.ShippingProviderId,
                methodCode = session.ShippingMethodCode,
                feeMinor = session.ShippingFeeMinor ?? 0,
            },
        };
        var responseJson = JsonSerializer.Serialize(response);

        // CR review on PR #30: cart consumption + session confirmation + idempotency persist
        // are now ONE atomic unit. Without this, a process crash between steps would leave a
        // confirmed payment / created order with a stale checkout session AND no cached replay
        // for the customer's retry. TransactionScope.ReadCommitted spans both DbContexts since
        // they share the same NpgsqlDataSource (CheckoutTestFactory + production wiring).
        using (var scope = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Required,
            new System.Transactions.TransactionOptions
            {
                IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted,
                Timeout = TimeSpan.FromSeconds(30),
            },
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            await cartDb.SaveChangesAsync(ct);
            await db.SaveChangesAsync(ct);
            await idempotencyStore.PersistAsync(idempotencyKey, accountId, normalizedBody, 200, responseJson, ct);
            scope.Complete();
        }
        claimFinalized = true;
        metrics.IncrementOutcome(session.MarketCode, "confirmed");
        metrics.RecordSubmitDuration(stopwatch.Elapsed.TotalMilliseconds, session.MarketCode, "confirmed");
        return Results.Json(response, statusCode: 200);
        }
        finally
        {
            if (!claimFinalized)
            {
                try { await idempotencyStore.ReleaseClaimAsync(idempotencyKey, ct); }
                catch { /* best-effort cleanup; TTL purges placeholder eventually anyway */ }
            }
        }
    }
}
