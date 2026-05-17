namespace BackendApi.Modules.Payments.Domain;

/// <summary>
/// Per-exception detail with operator-resolution workflow per
/// <c>data-model.md §reconciliation_exceptions</c>. State machine:
/// <c>open → resolved</c> with one of four documented actions.
/// </summary>
public sealed class ReconciliationException
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ProviderId { get; set; }

    /// <summary>jsonb snapshot of the provider's ledger row (when applicable).</summary>
    public string? ProviderLedgerRowJson { get; set; }

    public Guid? InternalPaymentId { get; set; }
    public decimal? InternalAmount { get; set; }
    public decimal? ProviderAmount { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public Guid? ResolvedBy { get; set; }
    public string? ResolutionNotes { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
