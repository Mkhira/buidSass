namespace BackendApi.Modules.Shipping.Domain;

/// <summary>
/// Operator-recorded delivery dispute per <c>data-model.md</c>
/// §shipment_disputes. AC-17 — operator marks dispute, then either creates
/// a re-delivery (linked shipment) or closes with refund.
/// </summary>
public sealed class ShipmentDispute
{
    public Guid Id { get; set; }
    public Guid ShipmentId { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
    public string ReportedBy { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ResolutionNotes { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
}
