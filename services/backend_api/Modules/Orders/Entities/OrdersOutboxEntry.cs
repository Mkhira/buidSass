namespace BackendApi.Modules.Orders.Entities;

/// <summary>
/// Transactional outbox row (research R7 + Catalog precedent). Written inside the order-mutating
/// transaction so events ride along atomically; a separate dispatcher worker publishes them.
/// </summary>
public sealed class OrdersOutboxEntry
{
    public long Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CommittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DispatchedAt { get; set; }
}
