using System.Text.Json;
using BackendApi.Modules.Returns.Entities;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Returns.Workers;

/// <summary>
/// Per-tick dispatch logic for the returns outbox. Extracted from the BackgroundService so
/// integration tests (Phase J — J7, J8) can drive a single tick synchronously instead of
/// waiting on the polling loop. The BackgroundService loops on this; production behaviour is
/// unchanged.
///
/// At-least-once semantics: every consumer (spec 011 advance, spec 012 credit-note issuer) is
/// idempotent on (returnRequestId, refundId, eventType).
/// </summary>
public sealed class ReturnsOutboxDispatchService(
    ReturnsDbContext db,
    IOrderRefundStateAdvancer orderRefundAdvancer,
    ICreditNoteIssuer creditNoteIssuer,
    ILogger<ReturnsOutboxDispatchService> logger)
{
    public const int BatchSize = 100;
    public const int MaxAttempts = 100;
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    public async Task<int> DispatchOnceAsync(CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var pending = await db.Outbox
            .Where(e => e.DispatchedAt == null
                && (e.NextAttemptAt == null || e.NextAttemptAt <= nowUtc))
            .OrderBy(e => e.CommittedAt)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (pending.Count == 0) return 0;

        foreach (var entry in pending)
        {
            try
            {
                await DispatchOneAsync(entry, ct);
                entry.DispatchedAt = DateTimeOffset.UtcNow;
                entry.LastError = null;
            }
            catch (Exception ex)
            {
                entry.DispatchAttempts += 1;
                entry.LastError = ex.Message;
                entry.NextAttemptAt = DateTimeOffset.UtcNow.Add(BackoffFor(entry.DispatchAttempts));
                logger.LogError(ex,
                    "returns.outbox.dispatch_failed id={Id} type={Type} attempt={Attempt}",
                    entry.Id, entry.EventType, entry.DispatchAttempts);
                if (entry.DispatchAttempts >= MaxAttempts)
                {
                    entry.NextAttemptAt = null;
                }
            }
        }
        await db.SaveChangesAsync(ct);
        return pending.Count;
    }

    private async Task DispatchOneAsync(ReturnsOutboxEntry entry, CancellationToken ct)
    {
        var eventType = entry.EventType.ToLowerInvariant();
        var payload = JsonDocument.Parse(entry.PayloadJson);
        switch (eventType)
        {
            case "return.submitted":
                {
                    var orderId = payload.RootElement.GetProperty("orderId").GetGuid();
                    var returnId = payload.RootElement.GetProperty("returnRequestId").GetGuid();
                    var advance = await orderRefundAdvancer.AdvanceAsync(new OrderRefundStateAdvanceRequest(
                        OrderId: orderId,
                        EventType: "return.submitted",
                        ReturnRequestId: returnId,
                        RefundId: null,
                        RefundedAmountMinor: 0,
                        ReturnedLineQtys: null), ct);
                    if (!advance.IsSuccess)
                    {
                        throw new InvalidOperationException(
                            $"orders.advance failed: {advance.ErrorCode} {advance.ErrorMessage}");
                    }
                    break;
                }
            case "return.rejected":
                {
                    var orderId = payload.RootElement.GetProperty("orderId").GetGuid();
                    var returnId = payload.RootElement.GetProperty("returnRequestId").GetGuid();
                    var openOthers = await db.ReturnRequests.AsNoTracking()
                        .AnyAsync(r => r.OrderId == orderId
                            && r.Id != returnId
                            && r.State != Primitives.ReturnStateMachine.Rejected
                            && r.State != Primitives.ReturnStateMachine.Refunded, ct);
                    if (openOthers)
                    {
                        logger.LogInformation("returns.outbox.rejected.other_open_rma orderId={Order}", orderId);
                        break;
                    }
                    var advance = await orderRefundAdvancer.AdvanceAsync(new OrderRefundStateAdvanceRequest(
                        OrderId: orderId,
                        EventType: "return.rejected",
                        ReturnRequestId: returnId,
                        RefundId: null,
                        RefundedAmountMinor: 0,
                        ReturnedLineQtys: null), ct);
                    if (!advance.IsSuccess)
                    {
                        throw new InvalidOperationException(
                            $"orders.advance failed: {advance.ErrorCode} {advance.ErrorMessage}");
                    }
                    break;
                }
            case "refund.completed":
            case "refund.manual_confirmed":
                {
                    var orderId = payload.RootElement.GetProperty("orderId").GetGuid();
                    var returnId = payload.RootElement.GetProperty("returnRequestId").GetGuid();
                    var refundId = payload.RootElement.GetProperty("refundId").GetGuid();
                    var amountMinor = payload.RootElement.GetProperty("amountMinor").GetInt64();
                    var deltas = new List<OrderRefundReturnedLine>();
                    if (payload.RootElement.TryGetProperty("lines", out var lineEl)
                        && lineEl.ValueKind == JsonValueKind.Array)
                    {
                        var returnLines = await db.ReturnLines.AsNoTracking()
                            .Where(rl => rl.ReturnRequestId == returnId)
                            .ToDictionaryAsync(rl => rl.Id, rl => rl.OrderLineId, ct);
                        foreach (var l in lineEl.EnumerateArray())
                        {
                            var rlId = l.GetProperty("returnLineId").GetGuid();
                            var qty = l.GetProperty("qty").GetInt32();
                            if (returnLines.TryGetValue(rlId, out var orderLineId) && qty > 0)
                            {
                                deltas.Add(new OrderRefundReturnedLine(orderLineId, qty));
                            }
                        }
                    }
                    var creditLines = deltas
                        .GroupBy(d => d.OrderLineId)
                        .Select(g => new CreditNoteIssueLine(g.Key, g.Sum(x => x.DeltaQty)))
                        .ToList();
                    if (creditLines.Count > 0)
                    {
                        var cn = await creditNoteIssuer.IssueForRefundAsync(new CreditNoteIssueRequest(
                            OrderId: orderId,
                            RefundId: refundId,
                            ReasonCode: $"return.{eventType}",
                            Lines: creditLines), ct);
                        if (!cn.IsSuccess)
                        {
                            throw new InvalidOperationException(
                                $"invoices.credit_note failed: {cn.ErrorCode} {cn.ErrorMessage}");
                        }
                    }
                    var advance = await orderRefundAdvancer.AdvanceAsync(new OrderRefundStateAdvanceRequest(
                        OrderId: orderId,
                        EventType: eventType,
                        ReturnRequestId: returnId,
                        RefundId: refundId,
                        RefundedAmountMinor: amountMinor,
                        ReturnedLineQtys: deltas), ct);
                    if (!advance.IsSuccess)
                    {
                        throw new InvalidOperationException(
                            $"orders.advance failed: {advance.ErrorCode} {advance.ErrorMessage}");
                    }
                    break;
                }
            default:
                logger.LogInformation(
                    "returns.outbox.dispatched id={Id} type={Type} aggregate={AggregateId}",
                    entry.Id, entry.EventType, entry.AggregateId);
                break;
        }
    }

    private static TimeSpan BackoffFor(int attempts)
    {
        var seconds = Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, Math.Min(attempts, 8)) * 5);
        return TimeSpan.FromSeconds(seconds);
    }
}
