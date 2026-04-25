using BackendApi.Modules.Orders.Primitives;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using FluentAssertions;

namespace Orders.Tests.Unit;

/// <summary>FR-018. Customer-facing high-level status derived from the four state machines.</summary>
public sealed class HighLevelStatusProjectorTests
{
    [Theory]
    [InlineData(OrderSm.Placed, PaymentSm.Authorized, FulfillmentSm.NotStarted, RefundSm.None, HighLevelStatusProjector.Processing)]
    [InlineData(OrderSm.Placed, PaymentSm.Captured, FulfillmentSm.Packed, RefundSm.None, HighLevelStatusProjector.Processing)]
    [InlineData(OrderSm.Placed, PaymentSm.Captured, FulfillmentSm.HandedToCarrier, RefundSm.None, HighLevelStatusProjector.Shipped)]
    [InlineData(OrderSm.Placed, PaymentSm.Captured, FulfillmentSm.Delivered, RefundSm.None, HighLevelStatusProjector.Delivered)]
    [InlineData(OrderSm.Placed, PaymentSm.PendingCod, FulfillmentSm.Picking, RefundSm.None, HighLevelStatusProjector.Processing)]
    [InlineData(OrderSm.Placed, PaymentSm.PendingBankTransfer, FulfillmentSm.NotStarted, RefundSm.None, HighLevelStatusProjector.PendingPayment)]
    [InlineData(OrderSm.Cancelled, PaymentSm.Voided, FulfillmentSm.Cancelled, RefundSm.None, HighLevelStatusProjector.Cancelled)]
    [InlineData(OrderSm.CancellationPending, PaymentSm.Captured, FulfillmentSm.Packed, RefundSm.Requested, HighLevelStatusProjector.CancellationPending)]
    [InlineData(OrderSm.Placed, PaymentSm.Refunded, FulfillmentSm.Delivered, RefundSm.Full, HighLevelStatusProjector.Refunded)]
    [InlineData(OrderSm.Placed, PaymentSm.PartiallyRefunded, FulfillmentSm.Delivered, RefundSm.Partial, HighLevelStatusProjector.PartiallyRefunded)]
    [InlineData(OrderSm.Placed, PaymentSm.Failed, FulfillmentSm.NotStarted, RefundSm.None, HighLevelStatusProjector.Failed)]
    public void Project_DerivesExpectedHighLevelStatus(string order, string payment, string fulfillment, string refund, string expected)
    {
        HighLevelStatusProjector.Project(order, payment, fulfillment, refund).Should().Be(expected);
    }
}
