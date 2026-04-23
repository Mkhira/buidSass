using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Catalog.Primitives.Outbox;
using BackendApi.Modules.Catalog.Primitives.Restriction;
using BackendApi.Modules.Catalog.Primitives.StateMachines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Catalog.Workers;

/// <summary>
/// Ticks every 30 seconds (SC-004 target ≤ 60 s). Claims due scheduled-publish rows, advances
/// the product state machine to Published, emits the outbox event, and invalidates restriction
/// cache entries. Uses per-row claim stamps so two worker instances cannot double-fire.
/// </summary>
public sealed class ScheduledPublishWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduledPublishWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly Guid SystemActorId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<ScheduledPublishWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "catalog.scheduled-publish-worker.cycle-failed");
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

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var outboxWriter = scope.ServiceProvider.GetRequiredService<CatalogOutboxWriter>();
        var auditEventPublisher = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
        var restrictionCache = scope.ServiceProvider.GetRequiredService<RestrictionCache>();

        var now = DateTimeOffset.UtcNow;
        var due = await dbContext.ScheduledPublishes
            .Where(s => s.PublishAt <= now && s.WorkerCompletedAt == null && s.WorkerClaimedAt == null)
            .OrderBy(s => s.PublishAt)
            .Take(25)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return;
        }

        _logger.LogInformation("catalog.scheduled-publish-worker.due count={Count}", due.Count);
        var claimStamp = DateTimeOffset.UtcNow;
        foreach (var schedule in due)
        {
            schedule.WorkerClaimedAt = claimStamp;
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        var machine = new ProductStateMachine();
        foreach (var schedule in due)
        {
            var product = await dbContext.Products.SingleOrDefaultAsync(p => p.Id == schedule.ProductId, cancellationToken);
            if (product is null)
            {
                schedule.WorkerCompletedAt = DateTimeOffset.UtcNow;
                continue;
            }

            if (!ProductStateMachine.TryParse(product.Status, out var from) || from != ProductState.Scheduled)
            {
                schedule.WorkerCompletedAt = DateTimeOffset.UtcNow;
                continue;
            }

            if (!machine.TryTransition(from, ProductTrigger.WorkerFire, out var next))
            {
                schedule.WorkerCompletedAt = DateTimeOffset.UtcNow;
                continue;
            }

            var previous = product.Status;
            product.Status = ProductStateMachine.Encode(next);
            product.PublishedAt ??= DateTimeOffset.UtcNow;
            dbContext.ProductStateTransitions.Add(new ProductStateTransition
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                FromStatus = previous,
                ToStatus = product.Status,
                ActorAccountId = SystemActorId,
                Reason = "scheduled_publish",
                OccurredAt = DateTimeOffset.UtcNow,
            });

            outboxWriter.Enqueue("catalog.product.published", product.Id, new { product.Id, product.Sku, product.MarketCodes, product.Restricted });
            restrictionCache.InvalidateProduct(product.Id);
            schedule.WorkerCompletedAt = DateTimeOffset.UtcNow;

            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: SystemActorId,
                    ActorRole: "system.catalog",
                    Action: "catalog.product.scheduled_publish_fired",
                    EntityType: nameof(Product),
                    EntityId: product.Id,
                    BeforeState: new { Status = previous },
                    AfterState: new { product.Status, product.PublishedAt },
                    Reason: "catalog.scheduled_publish.tick"),
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
