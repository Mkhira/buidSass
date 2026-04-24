using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Inventory.Workers;

public sealed class ExpiryWriteoffWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<InventoryOptions> options,
    ILogger<ExpiryWriteoffWorker> logger) : BackgroundService
{
    private static readonly Guid WorkerActorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IOptions<InventoryOptions> _options = options;
    private readonly ILogger<ExpiryWriteoffWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunWriteoffCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "inventory.expiry-writeoff-worker.cycle-failed");
            }

            var intervalSeconds = Math.Max(1, _options.Value.ExpiryWriteoffWorkerIntervalSeconds);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunWriteoffCycleAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var atsCalculator = scope.ServiceProvider.GetRequiredService<AtsCalculator>();
        var bucketMapper = scope.ServiceProvider.GetRequiredService<BucketMapper>();
        var reorderAlertEmitter = scope.ServiceProvider.GetRequiredService<ReorderAlertEmitter>();
        var availabilityEventEmitter = scope.ServiceProvider.GetRequiredService<AvailabilityEventEmitter>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();

        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var candidateBatchIds = await db.InventoryBatches
            .AsNoTracking()
            .Where(x => x.Status == "active" && x.ExpiryDate < todayUtc && x.QtyOnHand > 0)
            .OrderBy(x => x.ExpiryDate)
            .Select(x => x.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var batchId in candidateBatchIds)
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            var batch = await db.InventoryBatches
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM inventory.inventory_batches
                    WHERE "Id" = {batchId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);

            if (batch is null || !string.Equals(batch.Status, "active", StringComparison.OrdinalIgnoreCase) || batch.QtyOnHand <= 0 || batch.ExpiryDate >= todayUtc)
            {
                await tx.RollbackAsync(cancellationToken);
                continue;
            }

            var stock = await db.StockLevels
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM inventory.stock_levels
                    WHERE "ProductId" = {batch.ProductId}
                      AND "WarehouseId" = {batch.WarehouseId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);

            if (stock is null)
            {
                await tx.RollbackAsync(cancellationToken);
                continue;
            }

            var before = new
            {
                batch.Status,
                batch.QtyOnHand,
                stock.OnHand,
                stock.Reserved,
                stock.BucketCache,
            };

            // Expired batches MUST end with QtyOnHand=0 — a non-zero expired batch breaks the
            // invariant sum(active batches) == stock.OnHand. The stock delta only reflects what
            // actually leaves the stock aggregate (capped at stock.OnHand if drift exists).
            var stockDelta = Math.Min(batch.QtyOnHand, Math.Max(0, stock.OnHand));
            batch.Status = "expired";
            batch.QtyOnHand = 0;

            if (stockDelta <= 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                continue;
            }

            var bucketBefore = stock.BucketCache;
            var atsBefore = atsCalculator.Compute(stock.OnHand, stock.Reserved, stock.SafetyStock);
            stock.OnHand = Math.Max(0, stock.OnHand - stockDelta);
            stock.UpdatedAt = DateTimeOffset.UtcNow;
            var atsAfter = atsCalculator.Compute(stock.OnHand, stock.Reserved, stock.SafetyStock);
            stock.BucketCache = bucketMapper.Map(atsAfter);

            await reorderAlertEmitter.EmitIfCrossedAsync(
                db,
                stock,
                atsBefore,
                atsAfter,
                DateTimeOffset.UtcNow,
                _logger,
                cancellationToken);

            await availabilityEventEmitter.EmitIfChangedAsync(
                stock.ProductId,
                stock.WarehouseId,
                bucketBefore,
                stock.BucketCache,
                DateTimeOffset.UtcNow,
                cancellationToken);

            var movement = new InventoryMovement
            {
                ProductId = batch.ProductId,
                WarehouseId = batch.WarehouseId,
                BatchId = batch.Id,
                Kind = "writeoff",
                Delta = -stockDelta,
                Reason = "inventory.batch.expired",
                SourceKind = "worker",
                SourceId = null,
                ActorAccountId = WorkerActorId,
                OccurredAt = DateTimeOffset.UtcNow,
            };

            db.InventoryMovements.Add(movement);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            await audit.PublishAsync(new AuditEvent(
                WorkerActorId,
                "system",
                "inventory.batch.expired",
                nameof(InventoryBatch),
                batch.Id,
                before,
                new
                {
                    batch.Status,
                    batch.QtyOnHand,
                    stock.OnHand,
                    stock.Reserved,
                    stock.BucketCache,
                    MovementId = movement.Id,
                },
                "inventory.batch.expiry_writeoff"), cancellationToken);
        }
    }
}
