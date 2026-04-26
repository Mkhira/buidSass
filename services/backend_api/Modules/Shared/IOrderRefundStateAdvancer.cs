namespace BackendApi.Modules.Shared;

/// <summary>
/// Spec 013 → spec 011 seam (FR-010). Spec 013's <c>returns_outbox</c> dispatcher invokes
/// this on every refund-relevant lifecycle event so spec 011 can advance the order's
/// <c>refund_state</c>, increment <c>order_lines.returned_qty</c>, and emit
/// <c>payment.partially_refunded</c> / <c>payment.refunded</c>. Spec 011 owns those columns;
/// spec 013 must not write them directly.
///
/// Lives in <c>Modules/Shared/</c> so Returns and Orders never form a module dependency cycle.
/// Spec 011 ships the real implementation as an in-process adapter wrapping the
/// <c>POST /v1/internal/orders/{id}/advance-refund-state</c> handler logic.
/// </summary>
public interface IOrderRefundStateAdvancer
{
    Task<OrderRefundStateAdvanceResult> AdvanceAsync(
        OrderRefundStateAdvanceRequest request,
        CancellationToken cancellationToken);
}

public sealed record OrderRefundStateAdvanceRequest(
    Guid OrderId,
    /// <summary>Allowed: <c>return.submitted | return.rejected | refund.completed | refund.manual_confirmed</c>.</summary>
    string EventType,
    Guid? ReturnRequestId,
    Guid? RefundId,
    long RefundedAmountMinor,
    IReadOnlyList<OrderRefundReturnedLine>? ReturnedLineQtys);

public sealed record OrderRefundReturnedLine(Guid OrderLineId, int DeltaQty);

public sealed record OrderRefundStateAdvanceResult(
    bool IsSuccess,
    string? FinalRefundState,
    string? FinalPaymentState,
    string? ErrorCode,
    string? ErrorMessage);
