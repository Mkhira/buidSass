namespace BackendApi.Modules.Payments.Domain;

/// <summary>
/// One row per daily reconciliation run (spec §reconciliation_runs). The
/// <c>DailyReconciliationJob</c> creates a row in <c>running</c>, fans out to
/// every active provider's ledger fetcher, then sets totals + final status.
/// </summary>
public sealed class ReconciliationRun
{
    public Guid Id { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateOnly DateRangeStart { get; set; }
    public DateOnly DateRangeEnd { get; set; }

    /// <summary>JSON array of provider ids included in this run.</summary>
    public string ProvidersProcessedJson { get; set; } = "[]";

    public int InternalPaymentsCount { get; set; }
    public int ProviderLedgerRowsCount { get; set; }
    public int MatchedCount { get; set; }
    public int ExceptionsCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
