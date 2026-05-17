namespace BackendApi.Modules.Payments.Domain;

/// <summary>
/// Provider-initiated chargeback record per <c>data-model.md §chargebacks</c>.
/// BR-18 — v1 chargeback handling is operator-managed; no automated dispute
/// response. The state field tracks the operator workflow.
/// </summary>
public sealed class Chargeback
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public string ProviderChargebackId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? ReasonCode { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ResolutionNotes { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
