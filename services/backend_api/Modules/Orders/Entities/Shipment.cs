namespace BackendApi.Modules.Orders.Entities;

/// <summary>
/// One order may produce N shipments (research R4). Each ships with its own tracking +
/// state.  <c>State</c> values: created | handed_to_carrier | in_transit | out_for_delivery
/// | delivered | returned | failed.
/// </summary>
public sealed class Shipment
{
    public const string StateCreated = "created";
    public const string StateHandedToCarrier = "handed_to_carrier";
    public const string StateInTransit = "in_transit";
    public const string StateOutForDelivery = "out_for_delivery";
    public const string StateDelivered = "delivered";
    public const string StateReturned = "returned";
    public const string StateFailed = "failed";

    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string MethodCode { get; set; } = string.Empty;
    public string? TrackingNumber { get; set; }
    public string? CarrierLabelUrl { get; set; }
    public DateTimeOffset? EtaFrom { get; set; }
    public DateTimeOffset? EtaTo { get; set; }
    public string State { get; set; } = StateCreated;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? HandedToCarrierAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string PayloadJson { get; set; } = "{}";

    public Order? Order { get; set; }
    public List<ShipmentLine> Lines { get; set; } = new();
}
