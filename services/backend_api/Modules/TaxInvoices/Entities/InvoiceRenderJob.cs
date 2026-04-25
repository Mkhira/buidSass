namespace BackendApi.Modules.TaxInvoices.Entities;

/// <summary>
/// Async render queue (research R9). Worker claims with FOR UPDATE SKIP LOCKED, retries with
/// exponential backoff up to <c>MaxAttempts</c>. Either <see cref="InvoiceId"/> or
/// <see cref="CreditNoteId"/> is set, never both.
/// </summary>
public sealed class InvoiceRenderJob
{
    public const string StateQueued = "queued";
    public const string StateRendering = "rendering";
    public const string StateDone = "done";
    public const string StateFailed = "failed";
    public const int MaxAttempts = 6;

    public long Id { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? CreditNoteId { get; set; }
    /// <summary>Per-market partitioning (Principle 5 / ADR-010) — denormalised from the target.</summary>
    public string MarketCode { get; set; } = string.Empty;
    public string State { get; set; } = StateQueued;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
