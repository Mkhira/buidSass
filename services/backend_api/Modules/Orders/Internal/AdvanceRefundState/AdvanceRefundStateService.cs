using System.Text.Json;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Orders.Internal.AdvanceRefundState;

/// <summary>
/// Service-layer extraction of <c>AdvanceRefundState/Endpoint.cs</c>'s core logic so spec 013's
/// in-process <see cref="IOrderRefundStateAdvancer"/> adapter can call it without going through
/// HTTP. The HTTP endpoint also delegates here so the two paths stay byte-identical (single
/// source of truth for the over-refund guard, idempotency keying, line-qty bound check, and
/// state-machine validation).
/// </summary>
public sealed class AdvanceRefundStateService(OrdersDbContext db, ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("Orders.AdvanceRefundState.Service");

    public async Task<AdvanceOutcome> AdvanceAsync(
        Guid orderId,
        string eventType,
        Guid? returnRequestId,
        Guid? refundId,
        long refundedAmountMinor,
        IReadOnlyList<OrderRefundReturnedLine>? returnedLineQtys,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return AdvanceOutcome.Fail(400, "order.refund.invalid_request", "eventType is required");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // CR Critical round 3: ExecuteSqlInterpolatedAsync returns -1 for SELECT, so the
        // prior `if (locked == 0)` was dead code (worked only because the follow-up
        // FirstOrDefault caught missing rows). Use FromSqlInterpolated to materialise the
        // result AND acquire the FOR UPDATE lock atomically. AsNoTracking + Any() is
        // enough — we re-read with Include below for the lines collection; the lock is
        // held by the locking query for the duration of the tx.
        var lockedExists = await db.Orders
            .FromSqlInterpolated($"SELECT * FROM orders.orders WHERE \"Id\" = {orderId} FOR UPDATE")
            .AsNoTracking()
            .AnyAsync(ct);
        if (!lockedExists)
        {
            await tx.RollbackAsync(ct);
            return AdvanceOutcome.Fail(404, "order.not_found", "Order not found");
        }
        var order = await db.Orders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            await tx.RollbackAsync(ct);
            return AdvanceOutcome.Fail(404, "order.not_found", "Order not found");
        }

        // CR Critical round 5: normalize the event type ONCE up front. The switch was
        // case-insensitive but idempotencyKey, persisted Trigger, and SumPriorRefundsAsync
        // all used the raw eventType — so "refund.completed" and "REFUND.COMPLETED" got
        // different idempotency keys AND the prior-refund sum filter missed mixed-case
        // rows, reopening the over-refund path. All downstream code paths now use
        // normalizedEventType.
        var normalizedEventType = (eventType ?? string.Empty).Trim().ToLowerInvariant();
        var idempotencyKey = $"event={normalizedEventType} returnRequestId={returnRequestId} refundId={refundId}";
        var alreadySeen = await db.StateTransitions
            .AnyAsync(t => t.OrderId == orderId
                && t.Machine == OrderStateTransition.MachineRefund
                && t.Reason == idempotencyKey, ct);
        if (alreadySeen)
        {
            await tx.RollbackAsync(ct);
            return AdvanceOutcome.Ok(order.RefundState, order.PaymentState, deduped: true);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var fromRefund = order.RefundState;
        string? targetRefund = null;
        string? paymentEvent = null;

        switch (normalizedEventType)
        {
            case "return.submitted":
                if (string.Equals(order.RefundState, RefundSm.None, StringComparison.OrdinalIgnoreCase))
                {
                    targetRefund = RefundSm.Requested;
                }
                break;
            case "return.rejected":
                if (string.Equals(order.RefundState, RefundSm.Requested, StringComparison.OrdinalIgnoreCase))
                {
                    targetRefund = RefundSm.None;
                }
                break;
            case "refund.completed":
            case "refund.manual_confirmed":
                if (!string.Equals(order.PaymentState, PaymentSm.Captured, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(order.PaymentState, PaymentSm.PartiallyRefunded, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(order.PaymentState, PaymentSm.Refunded, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync(ct);
                    return AdvanceOutcome.Fail(409, "order.refund.payment_not_captured",
                        $"Refund event '{normalizedEventType}' is not valid for payment state '{order.PaymentState}'.");
                }
                if (refundId is null || refundedAmountMinor < 0)
                {
                    await tx.RollbackAsync(ct);
                    return AdvanceOutcome.Fail(400, "order.refund.invalid_request",
                        "refundId and non-negative refundedAmountMinor are required for refund events");
                }
                if (returnedLineQtys is { Count: > 0 } lineDeltas)
                {
                    foreach (var d in lineDeltas)
                    {
                        // CR Major: reject non-positive deltas — a negative would decrement
                        // ReturnedQty (corrupting the ledger) and a zero is meaningless.
                        if (d.DeltaQty <= 0)
                        {
                            await tx.RollbackAsync(ct);
                            return AdvanceOutcome.Fail(400, "order.refund.invalid_request",
                                $"OrderLine {d.OrderLineId} deltaQty must be positive (got {d.DeltaQty}).");
                        }
                        var line = order.Lines.FirstOrDefault(l => l.Id == d.OrderLineId);
                        if (line is null)
                        {
                            await tx.RollbackAsync(ct);
                            return AdvanceOutcome.Fail(404, "order.refund.line_not_found",
                                $"OrderLine {d.OrderLineId} not found on order {order.Id}");
                        }
                        var newReturned = line.ReturnedQty + d.DeltaQty;
                        if (newReturned + line.CancelledQty > line.Qty)
                        {
                            await tx.RollbackAsync(ct);
                            return AdvanceOutcome.Fail(409, "order.line.returned_qty_exceeds_delivered",
                                $"Line {line.Id}: returned ({newReturned}) + cancelled ({line.CancelledQty}) > qty ({line.Qty})");
                        }
                        line.ReturnedQty = newReturned;
                    }
                }
                var cumulativeBefore = await SumPriorRefundsAsync(orderId, ct);
                var cumulativeAfter = cumulativeBefore + refundedAmountMinor;
                if (cumulativeAfter > order.GrandTotalMinor)
                {
                    await tx.RollbackAsync(ct);
                    return AdvanceOutcome.Fail(409, "order.refund.over_refund_blocked",
                        $"Cumulative refund {cumulativeAfter} would exceed captured total {order.GrandTotalMinor}");
                }
                if (cumulativeAfter >= order.GrandTotalMinor)
                {
                    targetRefund = RefundSm.Full;
                    paymentEvent = "payment.refunded";
                    if (string.Equals(order.PaymentState, PaymentSm.Captured, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(order.PaymentState, PaymentSm.PartiallyRefunded, StringComparison.OrdinalIgnoreCase))
                    {
                        order.PaymentState = PaymentSm.Refunded;
                    }
                }
                else if (cumulativeAfter > 0)
                {
                    targetRefund = RefundSm.Partial;
                    paymentEvent = "payment.partially_refunded";
                    if (string.Equals(order.PaymentState, PaymentSm.Captured, StringComparison.OrdinalIgnoreCase))
                    {
                        order.PaymentState = PaymentSm.PartiallyRefunded;
                    }
                }
                break;
            default:
                await tx.RollbackAsync(ct);
                return AdvanceOutcome.Fail(400, "order.refund.invalid_event",
                    $"Unknown eventType '{normalizedEventType}'");
        }

        if (targetRefund is null)
        {
            await tx.RollbackAsync(ct);
            return AdvanceOutcome.Ok(order.RefundState, order.PaymentState, noop: true);
        }

        if (!RefundSm.IsValidTransition(fromRefund, targetRefund))
        {
            _logger.LogWarning(
                "orders.advance_refund_state.invalid_transition orderId={OrderId} from={From} to={To}",
                order.Id, fromRefund, targetRefund);
            await tx.RollbackAsync(ct);
            return AdvanceOutcome.Fail(409, "order.state.illegal_transition",
                $"Refund state transition {fromRefund} → {targetRefund} is not allowed");
        }

        order.RefundState = targetRefund;
        order.UpdatedAt = nowUtc;
        db.StateTransitions.Add(new OrderStateTransition
        {
            OrderId = order.Id,
            Machine = OrderStateTransition.MachineRefund,
            FromState = fromRefund,
            ToState = targetRefund,
            ActorAccountId = null,
            Trigger = $"returns.{normalizedEventType}",
            Reason = idempotencyKey,
            ContextJson = JsonSerializer.Serialize(new
            {
                returnRequestId,
                refundId,
                refundedAmountMinor,
            }),
            OccurredAt = nowUtc,
        });
        if (paymentEvent is not null)
        {
            db.Outbox.Add(new OrdersOutboxEntry
            {
                EventType = paymentEvent,
                AggregateId = order.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    orderId = order.Id,
                    orderNumber = order.OrderNumber,
                    refundedAmountMinor,
                    refundId,
                }),
                CommittedAt = nowUtc,
            });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return AdvanceOutcome.Ok(order.RefundState, order.PaymentState);
    }

    private async Task<long> SumPriorRefundsAsync(Guid orderId, CancellationToken ct)
    {
        var transitions = await db.StateTransitions.AsNoTracking()
            .Where(t => t.OrderId == orderId
                && t.Machine == OrderStateTransition.MachineRefund
                && (t.Trigger == "returns.refund.completed" || t.Trigger == "returns.refund.manual_confirmed"))
            .Select(t => t.ContextJson)
            .ToListAsync(ct);
        long total = 0;
        foreach (var ctx in transitions)
        {
            if (string.IsNullOrWhiteSpace(ctx)) continue;
            try
            {
                using var doc = JsonDocument.Parse(ctx);
                if (doc.RootElement.TryGetProperty("refundedAmountMinor", out var amt) && amt.TryGetInt64(out var v))
                {
                    total += v;
                }
            }
            catch (JsonException) { /* defensive */ }
        }
        return total;
    }
}

public sealed record AdvanceOutcome(
    bool IsSuccess,
    int StatusCode,
    string? FinalRefundState,
    string? FinalPaymentState,
    string? ReasonCode,
    string? Detail,
    bool Deduped,
    bool Noop)
{
    public static AdvanceOutcome Ok(string finalRefund, string finalPayment, bool deduped = false, bool noop = false)
        => new(true, 200, finalRefund, finalPayment, null, null, deduped, noop);
    public static AdvanceOutcome Fail(int status, string reasonCode, string detail)
        => new(false, status, null, null, reasonCode, detail, false, false);
}
