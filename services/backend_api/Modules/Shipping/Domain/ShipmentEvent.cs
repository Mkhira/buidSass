namespace BackendApi.Modules.Shipping.Domain;

/// <summary>
/// Per-shipment webhook history per <c>data-model.md</c> §shipment_events.
/// Every accepted webhook (after signature validation + idempotency check)
/// writes a row here. <see cref="InternalStateAtEvent"/> records the
/// shipment.state value AFTER precedence resolution (BR-12) — so lower-
/// precedence webhooks are preserved as history without regressing state.
/// </summary>
public sealed class ShipmentEvent
{
    public Guid Id { get; set; }
    public Guid ShipmentId { get; set; }
    public string ProviderEventKind { get; set; } = string.Empty;
    public string InternalStateAtEvent { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Provider payload stripped of PII; serialized JSON.</summary>
    public string RawPayloadRedactedJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
