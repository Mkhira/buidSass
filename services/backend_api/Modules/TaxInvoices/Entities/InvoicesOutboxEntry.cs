namespace BackendApi.Modules.TaxInvoices.Entities;

/// <summary>FR-016 — transactional outbox row. Dispatcher logs / publishes; spec 019
/// (notifications) consumes <c>invoice.issued</c> + <c>invoice.regenerated</c>.</summary>
public sealed class InvoicesOutboxEntry
{
    public long Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    /// <summary>Per-market partitioning (Principle 5 / ADR-010) — denormalised from the aggregate.</summary>
    public string MarketCode { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CommittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DispatchedAt { get; set; }
}
