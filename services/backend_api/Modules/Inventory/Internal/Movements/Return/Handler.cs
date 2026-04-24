using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Inventory.Internal.Movements.Return;

public static class Handler
{
    public sealed record Result(bool IsSuccess, int StatusCode, string? ReasonCode, string? Detail, ReturnMovementResponse? Response);

    public static async Task<Result> HandleAsync(
        ReturnMovementRequest request,
        InventoryDbContext db,
        AtsCalculator atsCalculator,
        BucketMapper bucketMapper,
        AvailabilityEventEmitter availabilityEventEmitter,
        IAuditEventPublisher auditEventPublisher,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        if (request.OrderId == Guid.Empty)
        {
            return new Result(false, 400, "inventory.invalid_order_id", "Order id is required.", null);
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return new Result(false, 400, "inventory.invalid_items", "At least one return item is required.", null);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var movementsAddedInThisCall = new List<InventoryMovement>();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var item in request.Items)
        {
            if (item.ProductId == Guid.Empty || item.WarehouseId == Guid.Empty || item.Qty <= 0)
            {
                await tx.RollbackAsync(cancellationToken);
                return new Result(false, 400, "inventory.invalid_items", "Each return item must include productId, warehouseId, and qty > 0.", null);
            }

            var stock = await db.StockLevels
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM inventory.stock_levels
                    WHERE "ProductId" = {item.ProductId}
                      AND "WarehouseId" = {item.WarehouseId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);

            if (stock is null)
            {
                stock = new StockLevel
                {
                    ProductId = item.ProductId,
                    WarehouseId = item.WarehouseId,
                    OnHand = 0,
                    Reserved = 0,
                    SafetyStock = 0,
                    ReorderThreshold = 0,
                    BucketCache = "out_of_stock",
                    UpdatedAt = nowUtc,
                };
                db.StockLevels.Add(stock);
            }

            InventoryBatch? batch = null;
            if (item.BatchId.HasValue && item.BatchId.Value != Guid.Empty)
            {
                batch = await db.InventoryBatches
                    .FromSqlInterpolated($"""
                        SELECT *
                        FROM inventory.inventory_batches
                        WHERE "Id" = {item.BatchId.Value}
                          AND "ProductId" = {item.ProductId}
                          AND "WarehouseId" = {item.WarehouseId}
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(cancellationToken);

                if (batch is null)
                {
                    // Caller referenced a batch that doesn't exist for this (product, warehouse).
                    // Fail explicitly rather than silently creating an arbitrary batch using the
                    // supplied Guid — which would otherwise land as a stray FK-less record.
                    await tx.RollbackAsync(cancellationToken);
                    return new Result(false, 404, "inventory.batch.not_found", "Referenced return batch does not exist for this product and warehouse.", null);
                }
            }

            if (batch is null)
            {
                // No batch provided → create a synthetic restock batch. Use the full orderId + a
                // per-batch GUID suffix to guarantee lot_no uniqueness across concurrent returns
                // that reference the same order (two items in one call, or partial returns).
                batch = new InventoryBatch
                {
                    Id = Guid.NewGuid(),
                    ProductId = item.ProductId,
                    WarehouseId = item.WarehouseId,
                    LotNo = $"RESTOCK-{request.OrderId:N}-{Guid.NewGuid():N}",
                    ExpiryDate = DateOnly.FromDateTime(nowUtc.UtcDateTime.Date.AddYears(3)),
                    QtyOnHand = 0,
                    Status = "active",
                    ReceivedAt = nowUtc,
                    ReceivedByAccountId = actorId == Guid.Empty ? null : actorId,
                    Notes = "restocked-from-return",
                };
                db.InventoryBatches.Add(batch);
            }

            var bucketBefore = stock.BucketCache;
            stock.OnHand += item.Qty;
            stock.UpdatedAt = nowUtc;
            var atsAfter = atsCalculator.Compute(stock.OnHand, stock.Reserved, stock.SafetyStock);
            stock.BucketCache = bucketMapper.Map(atsAfter);

            batch.QtyOnHand += item.Qty;
            if (!string.Equals(batch.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                batch.Status = "active";
            }

            await availabilityEventEmitter.EmitIfChangedAsync(
                stock.ProductId,
                stock.WarehouseId,
                bucketBefore,
                stock.BucketCache,
                nowUtc,
                cancellationToken);

            var movement = new InventoryMovement
            {
                ProductId = item.ProductId,
                WarehouseId = item.WarehouseId,
                BatchId = batch.Id,
                Kind = "return",
                Delta = item.Qty,
                Reason = request.ReasonCode,
                SourceKind = "return",
                SourceId = request.OrderId,
                ActorAccountId = actorId == Guid.Empty ? null : actorId,
                OccurredAt = nowUtc,
            };
            db.InventoryMovements.Add(movement);
            movementsAddedInThisCall.Add(movement);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // Movement ids are assigned by EF's IDENTITY-by-default after SaveChanges, so we can read
        // them directly. Previously this queried by SourceId=OrderId, which returned movements
        // from prior partial-return calls against the same order — confusing the caller.
        var movementIds = movementsAddedInThisCall.Select(m => m.Id).ToList();

        if (actorId != Guid.Empty)
        {
            await auditEventPublisher.PublishAsync(new AuditEvent(
                actorId,
                "internal",
                "inventory.movement.returned",
                nameof(InventoryMovement),
                request.OrderId,
                null,
                new
                {
                    request.OrderId,
                    ItemCount = request.Items.Count,
                    MovementIds = movementIds,
                },
                request.ReasonCode ?? "inventory.movement.return"), cancellationToken);
        }

        return new Result(true, 200, null, null, new ReturnMovementResponse(request.OrderId, movementIds));
    }
}
