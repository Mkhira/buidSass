using BackendApi.Modules.Shared;

namespace BackendApi.Modules.Orders.Internal.AdvanceRefundState;

/// <summary>
/// Spec 013 → spec 011 in-process adapter. Implements <see cref="IOrderRefundStateAdvancer"/>
/// by delegating to the same <see cref="AdvanceRefundStateService"/> the public HTTP endpoint
/// uses, so the over-refund guard and idempotency are byte-identical between the two entry
/// points. Registered in <c>OrdersModule.AddOrdersModule</c>.
/// </summary>
public sealed class OrderRefundStateAdvancerAdapter(AdvanceRefundStateService service) : IOrderRefundStateAdvancer
{
    public async Task<OrderRefundStateAdvanceResult> AdvanceAsync(
        OrderRefundStateAdvanceRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await service.AdvanceAsync(
            request.OrderId,
            request.EventType,
            request.ReturnRequestId,
            request.RefundId,
            request.RefundedAmountMinor,
            request.ReturnedLineQtys,
            cancellationToken);
        return new OrderRefundStateAdvanceResult(
            IsSuccess: outcome.IsSuccess,
            FinalRefundState: outcome.FinalRefundState,
            FinalPaymentState: outcome.FinalPaymentState,
            ErrorCode: outcome.ReasonCode,
            ErrorMessage: outcome.Detail);
    }
}
