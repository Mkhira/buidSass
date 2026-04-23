using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Catalog.Primitives.Outbox;

public interface ICatalogEventSubscriber
{
    Task PublishAsync(CatalogEventEnvelope envelope, CancellationToken cancellationToken);
}

public sealed record CatalogEventEnvelope(
    long OutboxId,
    string EventType,
    Guid AggregateId,
    string PayloadJson,
    DateTimeOffset CommittedAt);

/// <summary>
/// Default in-process subscriber. Spec 006 (search) will ship a real implementation that
/// re-indexes Meilisearch; until then we emit to the logger so dev/test environments can
/// observe outbox dispatch.
/// </summary>
public sealed class LoggingCatalogEventSubscriber(ILogger<LoggingCatalogEventSubscriber> logger) : ICatalogEventSubscriber
{
    private readonly ILogger<LoggingCatalogEventSubscriber> _logger = logger;

    public Task PublishAsync(CatalogEventEnvelope envelope, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "catalog.outbox.dispatched event={EventType} aggregate={AggregateId} committedAt={CommittedAt}",
            envelope.EventType,
            envelope.AggregateId,
            envelope.CommittedAt);
        return Task.CompletedTask;
    }
}
