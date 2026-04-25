using BackendApi.Modules.Orders.Primitives.StateMachines;

namespace BackendApi.Modules.Orders.Entities;

/// <summary>
/// Order aggregate root. Spec 011 data-model.md table 1. The four state columns are
/// independent (Principle 17) and validated by both DB CHECK constraints (in the migration)
/// and the in-process state machines under <c>Primitives/StateMachines/</c>.
/// </summary>
public sealed class Order
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;

    public long SubtotalMinor { get; set; }
    public long DiscountMinor { get; set; }
    public long TaxMinor { get; set; }
    public long ShippingMinor { get; set; }
    public long GrandTotalMinor { get; set; }

    public Guid PriceExplanationId { get; set; }
    public string? CouponCode { get; set; }

    public string ShippingAddressJson { get; set; } = "{}";
    public string BillingAddressJson { get; set; } = "{}";

    public string? B2bPoNumber { get; set; }
    public string? B2bReference { get; set; }
    public string? B2bNotes { get; set; }
    public DateTimeOffset? B2bRequestedDeliveryFrom { get; set; }
    public DateTimeOffset? B2bRequestedDeliveryTo { get; set; }

    public string OrderState { get; set; } = OrderSm.Placed;
    public string PaymentState { get; set; } = PaymentSm.Authorized;
    public string FulfillmentState { get; set; } = FulfillmentSm.NotStarted;
    public string RefundState { get; set; } = RefundSm.None;

    public DateTimeOffset PlacedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }

    public Guid? QuotationId { get; set; }
    public Guid? CheckoutSessionId { get; set; }

    public string? PaymentProviderId { get; set; }
    public string? PaymentProviderTxnId { get; set; }

    public string OwnerId { get; set; } = "platform";
    public Guid? VendorId { get; set; }
    public uint RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<OrderLine> Lines { get; set; } = new();
    public List<Shipment> Shipments { get; set; } = new();
}
