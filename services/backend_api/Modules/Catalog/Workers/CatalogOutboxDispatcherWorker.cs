using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Catalog.Primitives.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Catalog.Workers;

/// <summary>
/// Polls the catalog_outbox table every 2 seconds and dispatches undelivered rows to
/// every registered ICatalogEventSubscriber. Marks rows as dispatched only on successful
/// fan-out. At-least-once semantics — subscribers must be idempotent.
/// </summary>
public sealed class CatalogOutboxDispatcherWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<CatalogOutboxDispatcherWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<CatalogOutboxDispatcherWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "catalog.outbox-dispatcher.cycle-failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var subscribers = scope.ServiceProvider.GetServices<ICatalogEventSubscriber>().ToArray();

        if (subscribers.Length == 0)
        {
            return;
        }

        var batch = await dbContext.CatalogOutbox
            .Where(e => e.DispatchedAt == null)
            .OrderBy(e => e.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            return;
        }

        foreach (var entry in batch)
        {
            var envelope = new CatalogEventEnvelope(
                entry.Id,
                entry.EventType,
                entry.AggregateId,
                entry.PayloadJson,
                entry.CommittedAt);

            try
            {
                foreach (var subscriber in subscribers)
                {
                    await subscriber.PublishAsync(envelope, cancellationToken);
                }

                entry.DispatchedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "catalog.outbox-dispatcher.dispatch-failed outboxId={OutboxId} eventType={EventType}",
                    entry.Id,
                    entry.EventType);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
