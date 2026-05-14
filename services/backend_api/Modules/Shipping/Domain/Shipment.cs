namespace BackendApi.Modules.Shipping.Domain;

/// <summary>
/// Per-order shipment aggregate root per <c>data-model.md</c> §shipments.
/// BR-4 — one shipment per order at v1; multi-package deferred to Phase 2.
/// State transitions are gated by <see cref="StateMachines.ShipmentStateMachine"/>.
/// The PII-minimized recipient JSON (BR-15) lives in
/// <see cref="ShipToAddressRedactedJson"/>; raw recipient data never enters
/// this aggregate.
/// </summary>
public sealed class Shipment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public Guid MethodVersionId { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string? ProviderTrackingId { get; set; }

    /// <summary>SAS-signed URL surface; raw blob path is computed at read time.</summary>
    public string? LabelPdfBlobUrl { get; set; }

    public string State { get; set; } = string.Empty;

    /// <summary>PII-minimized shipping address (recipient name + last-4 phone + address). Per BR-15.</summary>
    public string ShipToAddressRedactedJson { get; set; } = "{}";

    /// <summary>FK to the original shipment when this row is a re-delivery (per US5).</summary>
    public Guid? ParentShipmentId { get; set; }

    public int Attempts { get; set; }
    public DateTimeOffset? EtaMin { get; set; }
    public DateTimeOffset? EtaMax { get; set; }
    public DateTimeOffset? LabelPurchasedAt { get; set; }
    public DateTimeOffset? HandedAt { get; set; }
    public DateTimeOffset? InTransitAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? FailedReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>EF Core RowVersion mapping (<c>xmin</c>) per project pattern.</summary>
    public uint Xmin { get; set; }
}
