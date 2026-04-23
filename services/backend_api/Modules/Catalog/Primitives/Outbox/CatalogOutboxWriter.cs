using System.Text.Json;
using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;

namespace BackendApi.Modules.Catalog.Primitives.Outbox;

public sealed class CatalogOutboxWriter(CatalogDbContext dbContext)
{
    private readonly CatalogDbContext _dbContext = dbContext;

    /// <summary>
    /// Writes a catalog outbox row within the current DbContext's change-tracker so that
    /// it commits in the same transaction as the aggregate mutation (transactional outbox).
    /// </summary>
    public void Enqueue<TPayload>(string eventType, Guid aggregateId, TPayload payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload);
        _dbContext.CatalogOutbox.Add(new CatalogOutboxEntry
        {
            EventType = eventType.Trim().ToLowerInvariant(),
            AggregateId = aggregateId,
            PayloadJson = payloadJson,
            CommittedAt = DateTimeOffset.UtcNow,
            DispatchedAt = null,
        });
    }
}
