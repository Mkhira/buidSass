using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Inventory.Admin.Common;
using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Inventory.Admin.Movements;

public sealed record AdjustRequest(
    Guid ProductId,
    Guid WarehouseId,
    Guid? BatchId,
    int Delta,
    string Reason);

public sealed record TransferRequest(
    Guid ProductId,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    int Qty,
    Guid? BatchId,
    string? Reason);

public sealed record WriteoffRequest(
    Guid ProductId,
    Guid WarehouseId,
    Guid BatchId,
    int Qty,
    string Reason);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapMovementEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/movements");
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        group.MapPost("/adjust", AdjustAsync).RequireAuthorization(adminAuth).RequirePermission("inventory.movement.write");
        group.MapPost("/transfer", TransferAsync).RequireAuthorization(adminAuth).RequirePermission("inventory.movement.write");
        group.MapPost("/writeoff", WriteoffAsync).RequireAuthorization(adminAuth).RequirePermission("inventory.movement.write");

        return builder;
    }

    private static async Task<IResult> AdjustAsync(
        AdjustRequest request,
        HttpContext context,
        InventoryDbContext db,
        AtsCalculator atsCalculator,
        BucketMapper bucketMapper,
        ReorderAlertEmitter reorderAlertEmitter,
        AvailabilityEventEmitter availabilityEventEmitter,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken ct)
    {
        if (request.ProductId == Guid.Empty || request.WarehouseId == Guid.Empty)
        {
            return AdminInventoryResponseFactory.Problem(context, 400, "inventory.invalid_items", "Invalid identifiers", "Product and warehouse identifiers are required.");
        }

        if (request.Delta == 0)
        {
            return AdminInventoryResponseFactory.Problem(context, 400, "inventory.invalid_qty", "Invalid adjustment", "Adjustment delta cannot be zero.");
        }

        var actorId = AdminInventoryResponseFactory.ResolveActorAccountId(context);
        var nowUtc = DateTimeOffset.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var stock = await db.StockLevels
            .FromSqlInterpolated($"""
                SELECT *
                FROM inventory.stock_levels
                WHERE "ProductId" = {request.ProductId}
                  AND "WarehouseId" = {request.WarehouseId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(ct);

        if (stock is null)
        {
            stock = new StockLevel
            {
                ProductId = request.ProductId,
                WarehouseId = request.WarehouseId,
                OnHand = 0,
                Reserved = 0,
                SafetyStock = 0,
                ReorderThreshold = 0,
                BucketCache = "out_of_stock",
                UpdatedAt = nowUtc,
            };
            db.StockLevels.Add(stock);
        }

        if (request.Delta < 0 && stock.OnHand + request.Delta < 0)
        {
            await tx.RollbackAsync(ct);
            return AdminInventoryResponseFactory.Problem(
                context,
                409,
                "inventory.negative_on_hand_blocked",
                "Negative on-hand blocked",
                "Adjustment would reduce on-hand stock below zero.");
        }

        InventoryBatch? batch = null;
        if (request.BatchId.HasValue)
        {
            batch = await db.InventoryBatches
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM inventory.inventory_batches
                    WHERE "Id" = {request.BatchId.Value}
                      AND "ProductId" = {request.ProductId}
                      AND "WarehouseId" = {request.WarehouseId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(ct);

            if (batch is null)
            {
                await tx.RollbackAsync(ct);
                return AdminInventoryResponseFactory.Problem(context, 404, "inventory.batch.not_found", "Batch not found", "The requested batch does not exist for this product and warehouse.");
            }

            if (request.Delta < 0 && batch.QtyOnHand + request.Delta < 0)
            {
                await tx.RollbackAsync(ct);
                return AdminInventoryResponseFactory.Problem(
                    context,
                    409,
                    "inventory.batch_qty_negative",
                    "Negative batch quantity",
                    "Adjustment would reduce batch quantity below zero.");
            }
        }

        var atsBefore = atsCalculator.Compute(stock.OnHand, stock.Reserved, stock.SafetyStock);
        var before = new
        {
            stock.OnHand,
            stock.Reserved,
            stock.BucketCache,
            BatchQtyOnHand = batch?.QtyOnHand,
        };
        var bucketBefore = stock.BucketCache;

        stock.OnHand += request.Delta;
        stock.UpdatedAt = nowUtc;

        if (batch is not null)
        {
            batch.QtyOnHand += request.Delta;
            if (batch.QtyOnHand <= 0)
            {
                batch.QtyOnHand = 0;
                batch.Status = "depleted";
            }
        }

        var atsAfter = atsCalculator.Compute(stock.OnHand, stock.Reserved, stock.SafetyStock);
        stock.BucketCache = bucketMapper.Map(atsAfter);

        await reorderAlertEmitter.EmitIfCrossedAsync(db, stock, atsBefore, atsAfter, nowUtc, context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("InventoryAdjust"), ct);
        await availabilityEventEmitter.EmitIfChangedAsync(
            stock.ProductId,
            stock.WarehouseId,
            bucketBefore,
            stock.BucketCache,
            nowUtc,
            ct);

        var movement = new InventoryMovement
        {
            ProductId = request.ProductId,
            WarehouseId = request.WarehouseId,
            BatchId = request.BatchId,
            Kind = "adjustment",
            Delta = request.Delta,
            Reason = request.Reason,
            SourceKind = "manual",
            SourceId = null,
            ActorAccountId = actorId == Guid.Empty ? null : actorId,
            OccurredAt = nowUtc,
        };

        db.InventoryMovements.Add(movement);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (actorId != Guid.Empty)
        {
            await auditEventPublisher.PublishAsync(new AuditEvent(
                actorId,
                "admin",
                "inventory.movement.adjusted",
                nameof(InventoryMovement),
                request.ProductId,
                before,
                new
                {
                    stock.OnHand,
                    stock.Reserved,
                    stock.BucketCache,
                    BatchQtyOnHand = batch?.QtyOnHand,
                    movement.Id,
                },
                request.Reason), ct);
        }

        return Results.Ok(new
        {
            stock.ProductId,
            stock.WarehouseId,
            stock.OnHand,
            stock.Reserved,
            stock.BucketCache,
            movementId = movement.Id,
        });
    }

    private static async Task<IResult> TransferAsync(
        TransferRequest request,
        HttpContext context,
        InventoryDbContext db,
        AtsCalculator atsCalculator,
        BucketMapper bucketMapper,
        ReorderAlertEmitter reorderAlertEmitter,
        AvailabilityEventEmitter availabilityEventEmitter,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken ct)
    {
        if (request.ProductId == Guid.Empty || request.FromWarehouseId == Guid.Empty || request.ToWarehouseId == Guid.Empty)
        {
            return AdminInventoryResponseFactory.Problem(context, 400, "inventory.invalid_items", "Invalid identifiers", "Product and warehouse identifiers are required.");
        }

        if (request.Qty <= 0)
        {
            return AdminInventoryResponseFactory.Problem(context, 400, "inventory.invalid_qty", "Invalid transfer quantity", "Transfer quantity must be greater than zero.");
        }

        if (request.FromWarehouseId == request.ToWarehouseId)
        {
            return AdminInventoryResponseFactory.Problem(context, 400, "inventory.invalid_items", "Invalid transfer warehouses", "Source and destination warehouses must be different.");
        }

        var actorId = AdminInventoryResponseFactory.ResolveActorAccountId(context);
        var nowUtc = DateTimeOffset.UtcNow;
        var transferId = Guid.NewGuid();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Deterministic lock order: always lock the lower warehouseId first, regardless of request
        // direction. Without this, concurrent Transfer(A→B) and Transfer(B→A) can deadlock.
        var firstWarehouseId = request.FromWarehouseId.CompareTo(request.ToWarehouseId) < 0
            ? request.FromWarehouseId
            : request.ToWarehouseId;
        var secondWarehouseId = firstWarehouseId == request.FromWarehouseId
            ? request.ToWarehouseId
            : request.FromWarehouseId;

        _ = await db.StockLevels
            .FromSqlInterpolated($"""
                SELECT *
                FROM inventory.stock_levels
                WHERE "ProductId" = {request.ProductId}
                  AND "WarehouseId" = {firstWarehouseId}
                FOR UPDATE
                """)
            .ToListAsync(ct);
        _ = await db.StockLevels
            .FromSqlInterpolated($"""
                SELECT *
                FROM inventory.stock_levels
                WHERE "ProductId" = {request.ProductId}
                  AND "WarehouseId" = {secondWarehouseId}
                FOR UPDATE
                """)
            .ToListAsync(ct);

        var fromStock = await db.StockLevels
            .SingleOrDefaultAsync(x => x.ProductId == request.ProductId && x.WarehouseId == request.FromWarehouseId, ct);

        if (fromStock is null || fromStock.OnHand < request.Qty)
        {
            await tx.RollbackAsync(ct);
            return AdminInventoryResponseFactory.Problem(
                context,
                409,
                "inventory.negative_on_hand_blocked",
                "Negative on-hand blocked",
                "Transfer would reduce source on-hand below zero.");
        }

        var toStock = await db.StockLevels
            .SingleOrDefaultAsync(x => x.ProductId == request.ProductId && x.WarehouseId == request.ToWarehouseId, ct);

        if (toStock is null)
        {
            toStock = new StockLevel
            {
                ProductId = request.ProductId,
                WarehouseId = request.ToWarehouseId,
                OnHand = 0,
                Reserved = 0,
                SafetyStock = 0,
                ReorderThreshold = 0,
                BucketCache = "out_of_stock",
                UpdatedAt = nowUtc,
            };
            db.StockLevels.Add(toStock);
        }

        // If the caller pinned a source batch, lock + decrement it; reject on insufficient qty
        // or mismatch with the source warehouse. Prevents stock/batch drift across Transfer.
        InventoryBatch? sourceBatch = null;
        if (request.BatchId.HasValue && request.BatchId.Value != Guid.Empty)
        {
            sourceBatch = await db.InventoryBatches
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM inventory.inventory_batches
                    WHERE "Id" = {request.BatchId.Value}
                      AND "ProductId" = {request.ProductId}
                      AND "WarehouseId" = {request.FromWarehouseId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(ct);

            if (sourceBatch is null)
            {
                await tx.RollbackAsync(ct);
                return AdminInventoryResponseFactory.Problem(context, 404, "inventory.batch.not_found", "Batch not found", "The requested batch does not exist in the source warehouse.");
            }
            if (sourceBatch.QtyOnHand < request.Qty)
            {
                await tx.RollbackAsync(ct);
                return AdminInventoryResponseFactory.Problem(context, 409, "inventory.batch_qty_negative", "Insufficient batch quantity", "Source batch has less quantity than the requested transfer.");
            }
        }

        var fromBucketBefore = fromStock.BucketCache;
        var fromAtsBefore = atsCalculator.Compute(fromStock.OnHand, fromStock.Reserved, fromStock.SafetyStock);
        fromStock.OnHand -= request.Qty;
        fromStock.UpdatedAt = nowUtc;
        var fromAtsAfter = atsCalculator.Compute(fromStock.OnHand, fromStock.Reserved, fromStock.SafetyStock);
        fromStock.BucketCache = bucketMapper.Map(fromAtsAfter);

        if (sourceBatch is not null)
        {
            sourceBatch.QtyOnHand -= request.Qty;
            if (sourceBatch.QtyOnHand == 0)
            {
                sourceBatch.Status = "depleted";
            }
        }

        var toBucketBefore = toStock.BucketCache;
        var toAtsBefore = atsCalculator.Compute(toStock.OnHand, toStock.Reserved, toStock.SafetyStock);
        toStock.OnHand += request.Qty;
        toStock.UpdatedAt = nowUtc;
        var toAtsAfter = atsCalculator.Compute(toStock.OnHand, toStock.Reserved, toStock.SafetyStock);
        toStock.BucketCache = bucketMapper.Map(toAtsAfter);

        var transferOut = new InventoryMovement
        {
            ProductId = request.ProductId,
            WarehouseId = request.FromWarehouseId,
            BatchId = request.BatchId,
            Kind = "transfer_out",
            Delta = -request.Qty,
            Reason = request.Reason,
            SourceKind = "manual",
            SourceId = transferId,
            ActorAccountId = actorId == Guid.Empty ? null : actorId,
            OccurredAt = nowUtc,
        };

        var transferIn = new InventoryMovement
        {
            ProductId = request.ProductId,
            WarehouseId = request.ToWarehouseId,
            BatchId = null,
            Kind = "transfer_in",
            Delta = request.Qty,
            Reason = request.Reason,
            SourceKind = "manual",
            SourceId = transferId,
            ActorAccountId = actorId == Guid.Empty ? null : actorId,
            OccurredAt = nowUtc,
        };

        db.InventoryMovements.Add(transferOut);
        db.InventoryMovements.Add(transferIn);

        await reorderAlertEmitter.EmitIfCrossedAsync(db, fromStock, fromAtsBefore, fromAtsAfter, nowUtc, context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("InventoryTransfer"), ct);
        await reorderAlertEmitter.EmitIfCrossedAsync(db, toStock, toAtsBefore, toAtsAfter, nowUtc, context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("InventoryTransfer"), ct);
        await availabilityEventEmitter.EmitIfChangedAsync(
            fromStock.ProductId,
            fromStock.WarehouseId,
            fromBucketBefore,
            fromStock.BucketCache,
            nowUtc,
            ct);
        await availabilityEventEmitter.EmitIfChangedAsync(
            toStock.ProductId,
            toStock.WarehouseId,
            toBucketBefore,
            toStock.BucketCache,
            nowUtc,
            ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (actorId != Guid.Empty)
        {
            await auditEventPublisher.PublishAsync(new AuditEvent(
                actorId,
                "admin",
                "inventory.movement.transferred",
                nameof(InventoryMovement),
                transferId,
                null,
                new
                {
                    request.ProductId,
                    request.FromWarehouseId,
                    request.ToWarehouseId,
                    request.Qty,
                    TransferOutMovementId = transferOut.Id,
                    TransferInMovementId = transferIn.Id,
                },
                request.Reason), ct);
        }

        return Results.Ok(new
        {
            transferId,
            transferOutMovementId = transferOut.Id,
            transferInMovementId = transferIn.Id,
        });
    }

    private static async Task<IResult> WriteoffAsync(
        WriteoffRequest request,
        HttpContext context,
        InventoryDbContext db,
        AtsCalculator atsCalculator,
        BucketMapper bucketMapper,
        ReorderAlertEmitter reorderAlertEmitter,
        AvailabilityEventEmitter availabilityEventEmitter,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken ct)
    {
        if (request.ProductId == Guid.Empty || request.WarehouseId == Guid.Empty || request.BatchId == Guid.Empty)
        {
            return AdminInventoryResponseFactory.Problem(context, 400, "inventory.invalid_items", "Invalid identifiers", "Product, warehouse and batch ids are required.");
        }

        if (request.Qty <= 0)
        {
            return AdminInventoryResponseFactory.Problem(context, 400, "inventory.invalid_qty", "Invalid writeoff quantity", "Writeoff quantity must be greater than zero.");
        }

        var actorId = AdminInventoryResponseFactory.ResolveActorAccountId(context);
        var nowUtc = DateTimeOffset.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var stock = await db.StockLevels
            .FromSqlInterpolated($"""
                SELECT *
                FROM inventory.stock_levels
                WHERE "ProductId" = {request.ProductId}
                  AND "WarehouseId" = {request.WarehouseId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(ct);

        var batch = await db.InventoryBatches
            .FromSqlInterpolated($"""
                SELECT *
                FROM inventory.inventory_batches
                WHERE "Id" = {request.BatchId}
                  AND "ProductId" = {request.ProductId}
                  AND "WarehouseId" = {request.WarehouseId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(ct);

        if (stock is null || batch is null)
        {
            await tx.RollbackAsync(ct);
            return AdminInventoryResponseFactory.Problem(context, 404, "inventory.batch.not_found", "Batch not found", "Stock or batch row does not exist for this product and warehouse.");
        }

        if (stock.OnHand < request.Qty || batch.QtyOnHand < request.Qty)
        {
            await tx.RollbackAsync(ct);
            return AdminInventoryResponseFactory.Problem(context, 409, "inventory.negative_on_hand_blocked", "Negative on-hand blocked", "Writeoff would reduce quantities below zero.");
        }

        var bucketBefore = stock.BucketCache;
        var atsBefore = atsCalculator.Compute(stock.OnHand, stock.Reserved, stock.SafetyStock);

        stock.OnHand -= request.Qty;
        stock.UpdatedAt = nowUtc;
        var atsAfter = atsCalculator.Compute(stock.OnHand, stock.Reserved, stock.SafetyStock);
        stock.BucketCache = bucketMapper.Map(atsAfter);

        batch.QtyOnHand -= request.Qty;
        if (batch.QtyOnHand == 0)
        {
            batch.Status = "depleted";
        }

        await reorderAlertEmitter.EmitIfCrossedAsync(db, stock, atsBefore, atsAfter, nowUtc, context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("InventoryWriteoff"), ct);
        await availabilityEventEmitter.EmitIfChangedAsync(
            stock.ProductId,
            stock.WarehouseId,
            bucketBefore,
            stock.BucketCache,
            nowUtc,
            ct);

        var movement = new InventoryMovement
        {
            ProductId = request.ProductId,
            WarehouseId = request.WarehouseId,
            BatchId = request.BatchId,
            Kind = "writeoff",
            Delta = -request.Qty,
            Reason = request.Reason,
            SourceKind = "manual",
            SourceId = null,
            ActorAccountId = actorId == Guid.Empty ? null : actorId,
            OccurredAt = nowUtc,
        };

        db.InventoryMovements.Add(movement);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (actorId != Guid.Empty)
        {
            await auditEventPublisher.PublishAsync(new AuditEvent(
                actorId,
                "admin",
                "inventory.movement.writeoff",
                nameof(InventoryMovement),
                request.ProductId,
                null,
                new
                {
                    request.ProductId,
                    request.WarehouseId,
                    request.BatchId,
                    request.Qty,
                    movement.Id,
                },
                request.Reason), ct);
        }

        return Results.Ok(new
        {
            movementId = movement.Id,
            stock.ProductId,
            stock.WarehouseId,
            stock.OnHand,
            stock.Reserved,
            stock.BucketCache,
        });
    }
}
