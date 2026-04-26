namespace BackendApi.Modules.Returns.Entities;

/// <summary>data-model.md table 9. Mirrors <c>OrdersOutboxEntry</c> (research R7).</summary>
public sealed class ReturnsOutboxEntry
{
    public long Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CommittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DispatchedAt { get; set; }
    public int DispatchAttempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}
