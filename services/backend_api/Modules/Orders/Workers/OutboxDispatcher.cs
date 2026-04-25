using BackendApi.Modules.Orders.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Orders.Workers;

/// <summary>
/// G3 — transactional outbox dispatcher. Polls <c>orders.orders_outbox WHERE dispatched_at IS NULL</c>
/// and marks rows dispatched. Subscribers (spec 012 invoices, spec 014 notifications, search
/// analytics) will consume these via their own bridges in follow-up specs; for now this
/// dispatcher records the event in structured logs so operators can see the trail.
///
/// At-least-once semantics: a crash between log emission and DispatchedAt update will replay
/// the same event next tick. Subscribers MUST be idempotent.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceProvider services,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("orders.outbox_dispatcher.started interval={Interval}s batch={Batch}",
            PollInterval.TotalSeconds, BatchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchBatchAsync(stoppingToken);
                if (dispatched == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "orders.outbox_dispatcher.error");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var pending = await db.Outbox
            .Where(e => e.DispatchedAt == null)
            .OrderBy(e => e.CommittedAt)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (pending.Count == 0) return 0;

        var nowUtc = DateTimeOffset.UtcNow;
        foreach (var entry in pending)
        {
            // Spec 012/014/search will subscribe in follow-ups. For now, log so the trail is
            // observable in stdout / Serilog console formatting.
            logger.LogInformation(
                "orders.outbox.dispatched id={Id} type={Type} aggregate={AggregateId}",
                entry.Id, entry.EventType, entry.AggregateId);
            entry.DispatchedAt = nowUtc;
        }
        await db.SaveChangesAsync(ct);
        return pending.Count;
    }
}
