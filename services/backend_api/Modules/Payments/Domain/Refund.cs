namespace BackendApi.Modules.Payments.Domain;

/// <summary>
/// Per-refund record (1:N from Payment) per <c>data-model.md §refunds</c>.
/// V-5 — <c>SUM(refunds.amount WHERE state IN ('pending','completed')) &lt;= payments.amount</c>
/// is enforced both at app layer (pre-check) and DB layer (trigger on insert/update).
/// </summary>
public sealed class Refund
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string State { get; set; } = string.Empty;
    public string? ProviderRefundId { get; set; }
    public Guid InitiatedBy { get; set; }
    public string? FailedReason { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
