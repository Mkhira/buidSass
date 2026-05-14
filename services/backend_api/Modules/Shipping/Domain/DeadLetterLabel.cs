namespace BackendApi.Modules.Shipping.Domain;

/// <summary>
/// Quarantine row for shipments that exhausted the 3-attempt label-creation
/// retry budget per <c>data-model.md</c> §dead_letter_labels. AC-8 / BR-5 —
/// resolution is operator-driven (retry, discard, manual_label); no auto-
/// cascade across providers (BR-11).
/// </summary>
public sealed class DeadLetterLabel
{
    public Guid ShipmentId { get; set; }
    public string? LastErrorMessageRedacted { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset EnteredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? Resolution { get; set; }
    public Guid? ResolvedBy { get; set; }
}
