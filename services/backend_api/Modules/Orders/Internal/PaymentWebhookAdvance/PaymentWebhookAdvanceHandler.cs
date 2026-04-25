using System.Text.Json;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Orders.Internal.PaymentWebhookAdvance;

/// <summary>
/// FR-007 / FR-024 / SC-005 — F1 wiring. Checkout's payment-gateway webhook (spec 010) calls
/// this seam after it advances a PaymentAttempt so the Order aggregate's <c>payment_state</c>
/// stays in lock-step. Webhook dedup is upstream (spec 010's payment_webhook_events unique
/// constraint), so by the time we land here this is the FIRST observation of this event.
/// </summary>
public sealed class PaymentWebhookAdvanceHandler(
    OrdersDbContext db,
    ILogger<PaymentWebhookAdvanceHandler> logger) : IOrderPaymentStateHook
{
    public async Task<OrderPaymentAdvanceResult> AdvanceFromAttemptAsync(
        OrderPaymentAdvanceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderId) || string.IsNullOrWhiteSpace(request.ProviderTxnId))
        {
            return new OrderPaymentAdvanceResult(false, null, null);
        }

        var order = await db.Orders.FirstOrDefaultAsync(
            o => o.PaymentProviderId == request.ProviderId && o.PaymentProviderTxnId == request.ProviderTxnId, ct);
        if (order is null)
        {
            logger.LogWarning(
                "orders.webhook_advance.no_matching_order providerId={ProviderId} txnId={TxnId}",
                request.ProviderId, request.ProviderTxnId);
            return new OrderPaymentAdvanceResult(false, null, null);
        }

        var targetState = MapAttemptStateToOrderPaymentState(request.MappedAttemptState, order.PaymentState);
        if (targetState is null)
        {
            // No domain advance for this attempt state (e.g., 'initiated' or 'pending_webhook').
            return new OrderPaymentAdvanceResult(true, order.PaymentState, order.Id);
        }

        var fromState = order.PaymentState;
        if (string.Equals(fromState, targetState, StringComparison.OrdinalIgnoreCase))
        {
            // SC-005: duplicate webhook deliveries land on the same target state — idempotent.
            return new OrderPaymentAdvanceResult(true, order.PaymentState, order.Id);
        }
        if (!PaymentSm.IsValidTransition(fromState, targetState))
        {
            logger.LogWarning(
                "orders.webhook_advance.invalid_transition orderId={OrderId} from={From} to={To}",
                order.Id, fromState, targetState);
            return new OrderPaymentAdvanceResult(true, order.PaymentState, order.Id);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        order.PaymentState = targetState;
        order.UpdatedAt = nowUtc;

        db.StateTransitions.Add(new OrderStateTransition
        {
            OrderId = order.Id,
            Machine = OrderStateTransition.MachinePayment,
            FromState = fromState,
            ToState = targetState,
            ActorAccountId = null,
            Trigger = "webhook.payment_gateway",
            Reason = $"providerEventId={request.ProviderEventId} attemptState={request.MappedAttemptState} {request.ErrorCode}".Trim(),
            OccurredAt = nowUtc,
        });

        // FR-015: payment.captured triggers spec 012's invoice issuance. Other transitions
        // (failed/voided/refunded) emit their own outbox events for audit/notifications.
        var eventName = targetState switch
        {
            var s when string.Equals(s, PaymentSm.Captured, StringComparison.OrdinalIgnoreCase) => "payment.captured",
            var s when string.Equals(s, PaymentSm.Failed, StringComparison.OrdinalIgnoreCase) => "payment.failed",
            var s when string.Equals(s, PaymentSm.Voided, StringComparison.OrdinalIgnoreCase) => "payment.voided",
            var s when string.Equals(s, PaymentSm.Refunded, StringComparison.OrdinalIgnoreCase) => "payment.refunded",
            var s when string.Equals(s, PaymentSm.PartiallyRefunded, StringComparison.OrdinalIgnoreCase) => "payment.partially_refunded",
            var s when string.Equals(s, PaymentSm.Authorized, StringComparison.OrdinalIgnoreCase) => "payment.authorized",
            _ => "payment.state_changed",
        };
        db.Outbox.Add(new OrdersOutboxEntry
        {
            EventType = eventName,
            AggregateId = order.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                fromState,
                toState = targetState,
                providerEventId = request.ProviderEventId,
                errorCode = request.ErrorCode,
                amountMinor = order.GrandTotalMinor,
                currency = order.Currency,
                at = nowUtc,
            }),
            CommittedAt = nowUtc,
            DispatchedAt = null,
        });

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            // Another caller advanced concurrently; reread.
            await db.Entry(order).ReloadAsync(ct);
        }

        logger.LogInformation(
            "orders.webhook_advance.applied orderId={OrderId} from={From} to={To} event={Event}",
            order.Id, fromState, targetState, eventName);
        return new OrderPaymentAdvanceResult(true, order.PaymentState, order.Id);
    }

    /// <summary>
    /// Map Checkout's PaymentAttempt state → Order's PaymentSm domain state. Returns null if
    /// no order-level advance applies (e.g., 'initiated' or 'pending_webhook' are
    /// per-attempt-only).
    /// </summary>
    private static string? MapAttemptStateToOrderPaymentState(string attemptState, string currentOrderState)
    {
        if (string.Equals(attemptState, "captured", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentSm.Captured;
        }
        if (string.Equals(attemptState, "authorized", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentSm.Authorized;
        }
        if (string.Equals(attemptState, "voided", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentSm.Voided;
        }
        if (string.Equals(attemptState, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(attemptState, "declined", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentSm.Failed;
        }
        if (string.Equals(attemptState, "refunded", StringComparison.OrdinalIgnoreCase))
        {
            // Spec 013 owns refunds; for an attempt-level refund we conservatively flip to
            // Refunded. Partial-refund nuance comes from spec 013's advance-refund-state seam.
            return PaymentSm.Refunded;
        }
        // initiated, pending_webhook → no order-level advance.
        return null;
    }
}
