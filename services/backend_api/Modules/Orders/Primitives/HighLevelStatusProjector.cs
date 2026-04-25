using BackendApi.Modules.Orders.Primitives.StateMachines;

namespace BackendApi.Modules.Orders.Primitives;

/// <summary>
/// FR-018 + research R9. Derives a single user-facing status word from the four independent
/// state machines. Storefront UX consumes this; admin UX shows the full four-state truth.
/// Pure function — easy to test (SC-003 fuzz harness uses this to detect derivation drift).
/// </summary>
public static class HighLevelStatusProjector
{
    public const string PendingPayment = "pending_payment";
    public const string Processing = "processing";
    public const string Shipped = "shipped";
    public const string Delivered = "delivered";
    public const string Cancelled = "cancelled";
    public const string CancellationPending = "cancellation_pending";
    public const string PartiallyRefunded = "partially_refunded";
    public const string Refunded = "refunded";
    public const string Failed = "failed";

    public static string Project(string orderState, string paymentState, string fulfillmentState, string refundState)
    {
        // Order-level terminal first.
        if (string.Equals(orderState, OrderSm.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            return Cancelled;
        }
        if (string.Equals(orderState, OrderSm.CancellationPending, StringComparison.OrdinalIgnoreCase))
        {
            return CancellationPending;
        }

        // Refund overrides fulfillment narrative once money has moved back.
        if (string.Equals(refundState, RefundSm.Full, StringComparison.OrdinalIgnoreCase)
            || string.Equals(paymentState, PaymentSm.Refunded, StringComparison.OrdinalIgnoreCase))
        {
            return Refunded;
        }
        if (string.Equals(refundState, RefundSm.Partial, StringComparison.OrdinalIgnoreCase)
            || string.Equals(paymentState, PaymentSm.PartiallyRefunded, StringComparison.OrdinalIgnoreCase))
        {
            return PartiallyRefunded;
        }

        // Payment failures dominate.
        if (string.Equals(paymentState, PaymentSm.Failed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(paymentState, PaymentSm.Voided, StringComparison.OrdinalIgnoreCase))
        {
            return Failed;
        }

        // Fulfillment narrative.
        if (string.Equals(fulfillmentState, FulfillmentSm.Delivered, StringComparison.OrdinalIgnoreCase))
        {
            return Delivered;
        }
        if (string.Equals(fulfillmentState, FulfillmentSm.HandedToCarrier, StringComparison.OrdinalIgnoreCase))
        {
            return Shipped;
        }
        if (string.Equals(fulfillmentState, FulfillmentSm.Picking, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fulfillmentState, FulfillmentSm.Packed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fulfillmentState, FulfillmentSm.AwaitingStock, StringComparison.OrdinalIgnoreCase))
        {
            return Processing;
        }

        // Default: depends on payment-state. COD / bank transfer → pending_payment; otherwise processing.
        return PaymentSm.IsPending(paymentState) ? PendingPayment : Processing;
    }
}
