namespace BackendApi.Modules.TaxInvoices.Entities;

/// <summary>
/// Cross-module subscription watermark — spec 012 polls spec 011's
/// <c>orders.orders_outbox</c> for <c>payment.captured</c> events and stores the highest
/// outbox row id processed per (source_module, event_type). On worker restart we resume
/// from this watermark instead of re-processing the entire history.
/// </summary>
public sealed class SubscriptionCheckpoint
{
    public string SourceModule { get; set; } = string.Empty; // e.g. "orders"
    public string EventType { get; set; } = string.Empty;    // e.g. "payment.captured"
    public long LastObservedOutboxId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
