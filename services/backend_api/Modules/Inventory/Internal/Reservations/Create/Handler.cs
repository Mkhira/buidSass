using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Observability;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using BackendApi.Modules.Inventory.Primitives.Fefo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Inventory.Internal.Reservations.Create;

public static class Handler
{
    public sealed record Result(
        bool IsSuccess,
        int StatusCode,
        string? ReasonCode,
        string? Detail,
        CreateReservationResponse? Response,
        IDictionary<string, object?>? Extensions = null);

    public static async Task<Result> HandleAsync(
        CreateReservationRequest request,
        InventoryDbContext inventoryDb,
        CatalogDbContext catalogDb,
        AtsCalculator atsCalculator,
        BucketMapper bucketMapper,
        FefoPicker fefoPicker,
        ReorderAlertEmitter reorderAlertEmitter,
        AvailabilityEventEmitter availabilityEventEmitter,
        InventoryMetrics inventoryMetrics,
        IAuditEventPublisher auditEventPublisher,
        IOptions<InventoryOptions> inventoryOptions,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        if (request.Items is null || request.Items.Count != 1)
        {
            return new Result(false, 400, "inventory.invalid_items", "Exactly one item per reservation request is supported in v1.", null);
        }

        var item = request.Items[0];
        if (item.ProductId == Guid.Empty || item.Qty <= 0)
        {
            return new Result(false, 400, "inventory.invalid_qty", "Quantity must be greater than zero.", null);
        }

        if (string.IsNullOrWhiteSpace(request.MarketCode))
        {
            return new Result(false, 400, "inventory.warehouse_market_mismatch", "Market code is required.", null);
        }

        var marketCode = request.MarketCode.Trim().ToLowerInvariant();
        var warehouse = await inventoryDb.Warehouses
            .AsNoTracking()
            .Where(x => x.IsActive && x.MarketCode == marketCode)
            .OrderBy(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);

        if (warehouse is null)
        {
            return new Result(false, 400, "inventory.warehouse_market_mismatch", "No active warehouse is configured for the provided market.", null);
        }

        var product = await catalogDb.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == item.ProductId, cancellationToken);

        if (product is null || !product.MarketCodes.Any(m => string.Equals(m, marketCode, StringComparison.OrdinalIgnoreCase)))
        {
            return new Result(false, 400, "inventory.warehouse_market_mismatch", "Product is not available for the requested market.", null);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var options = inventoryOptions.Value;
        var ttlMinutes = options.ReservationTtlMinutes <= 0 ? 15 : options.ReservationTtlMinutes;

        await using var tx = await inventoryDb.Database.BeginTransactionAsync(cancellationToken);

        var stock = await inventoryDb.StockLevels
            .FromSqlInterpolated($"""
                SELECT *
                FROM inventory.stock_levels
                WHERE "ProductId" = {item.ProductId}
                  AND "WarehouseId" = {warehouse.Id}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (stock is null)
        {
            await tx.RollbackAsync(cancellationToken);
            logger.LogInformation(
                "inventory.reservation.create warehouseId={WarehouseId} productId={ProductId} qty={Qty} bucketBefore={BucketBefore} bucketAfter={BucketAfter} reservationId={ReservationId} outcome={Outcome}",
                warehouse.Id,
                item.ProductId,
                item.Qty,
                "out_of_stock",
                "out_of_stock",
                Guid.Empty,
                "exhausted");
            inventoryMetrics.IncrementReservationConflict(warehouse.Id, item.ProductId);
            inventoryMetrics.RecordReservationDuration((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds, warehouse.Id, item.ProductId, "exhausted");
            return BuildInsufficient(item.ProductId, item.Qty, item.Qty);
        }

        var activeStatus = "active";
        // Exclude already-expired batches from FEFO. The expiry-writeoff worker runs on a
        // schedule, so a batch can pass its expiry date before the next tick; without this
        // predicate the cart could reserve expired stock.
        var todayUtc = DateOnly.FromDateTime(nowUtc.UtcDateTime.Date);
        var pickedBatch = await inventoryDb.InventoryBatches
            .FromSqlInterpolated($"""
                SELECT *
                FROM inventory.inventory_batches
                WHERE "ProductId" = {item.ProductId}
                  AND "WarehouseId" = {warehouse.Id}
                  AND "Status" = {activeStatus}
                  AND "ExpiryDate" >= {todayUtc}
                  AND "QtyOnHand" > 0
                ORDER BY "ExpiryDate" ASC, "Id" ASC
                LIMIT 1
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        var batch = fefoPicker.PickBatch(pickedBatch);
        var atsBefore = atsCalculator.Compute(stock.OnHand, stock.Reserved, stock.SafetyStock);
        // V1 pins a reservation to a single batch. If the FEFO-picked batch holds less than the
        // requested qty, reject rather than let Convert silently clamp the batch decrement later
        // (which caused stock_levels and sum(batches.qty_on_hand) to drift). Cross-batch
        // reservations are deferred to spec 011.
        if (batch is null || atsBefore < item.Qty || batch.QtyOnHand < item.Qty)
        {
            await tx.RollbackAsync(cancellationToken);
            var shortfall = Math.Max(0, item.Qty - Math.Max(0, atsBefore));
            logger.LogInformation(
                "inventory.reservation.create warehouseId={WarehouseId} productId={ProductId} qty={Qty} bucketBefore={BucketBefore} bucketAfter={BucketAfter} reservationId={ReservationId} outcome={Outcome}",
                warehouse.Id,
                item.ProductId,
                item.Qty,
                stock.BucketCache,
                stock.BucketCache,
                Guid.Empty,
                "exhausted");
            inventoryMetrics.IncrementReservationConflict(warehouse.Id, item.ProductId);
            inventoryMetrics.RecordReservationDuration((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds, warehouse.Id, item.ProductId, "exhausted");
            return BuildInsufficient(item.ProductId, item.Qty, shortfall == 0 ? item.Qty : shortfall);
        }

        var bucketBefore = stock.BucketCache;
        stock.Reserved += item.Qty;
        var atsAfter = atsCalculator.Compute(stock.OnHand, stock.Reserved, stock.SafetyStock);
        stock.BucketCache = bucketMapper.Map(atsAfter);
        stock.UpdatedAt = nowUtc;

        await reorderAlertEmitter.EmitIfCrossedAsync(
            inventoryDb,
            stock,
            atsBefore,
            atsAfter,
            nowUtc,
            logger,
            cancellationToken);

        await availabilityEventEmitter.EmitIfChangedAsync(
            stock.ProductId,
            stock.WarehouseId,
            bucketBefore,
            stock.BucketCache,
            nowUtc,
            cancellationToken);

        var reservationId = Guid.NewGuid();
        var expiresAt = nowUtc.AddMinutes(ttlMinutes);

        var reservation = new InventoryReservation
        {
            Id = reservationId,
            ProductId = item.ProductId,
            WarehouseId = warehouse.Id,
            MarketCode = marketCode,
            Qty = item.Qty,
            CartId = request.CartId,
            OrderId = null,
            PickedBatchId = batch.Id,
            AccountId = request.AccountId,
            Status = "active",
            ExpiresAt = expiresAt,
            CreatedAt = nowUtc,
        };

        inventoryDb.InventoryReservations.Add(reservation);
        await inventoryDb.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var actorId = ResolveActorId(request.AccountId);
        if (actorId != Guid.Empty)
        {
            await auditEventPublisher.PublishAsync(new AuditEvent(
                actorId,
                "internal",
                "inventory.reservation.created",
                nameof(InventoryReservation),
                reservation.Id,
                null,
                new
                {
                    reservation.ProductId,
                    reservation.WarehouseId,
                    reservation.Qty,
                    reservation.PickedBatchId,
                    reservation.ExpiresAt,
                    reservation.Status,
                },
                "inventory.reservation.create"), cancellationToken);
        }

        logger.LogInformation(
            "inventory.reservation.create warehouseId={WarehouseId} productId={ProductId} qty={Qty} bucketBefore={BucketBefore} bucketAfter={BucketAfter} reservationId={ReservationId} outcome={Outcome}",
            warehouse.Id,
            item.ProductId,
            item.Qty,
            bucketMapper.Map(atsBefore),
            stock.BucketCache,
            reservation.Id,
            "success");
        inventoryMetrics.RecordReservationDuration((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds, warehouse.Id, item.ProductId, "success");
        inventoryMetrics.ObserveAts(stock.WarehouseId, stock.ProductId, atsAfter);

        return new Result(
            true,
            StatusCodes.Status200OK,
            null,
            null,
            new CreateReservationResponse(
                reservation.Id,
                [new CreateReservationItemResponse(item.ProductId, item.Qty, batch.Id, expiresAt)]));
    }

    private static Result BuildInsufficient(Guid productId, int requestedQty, int shortfall)
    {
        var shortfallByProduct = new Dictionary<string, int>
        {
            [productId.ToString("D")] = shortfall,
        };

        return new Result(
            false,
            StatusCodes.Status409Conflict,
            "inventory.insufficient",
            "Requested quantity exceeds available-to-sell stock.",
            null,
            new Dictionary<string, object?>
            {
                ["shortfallByProduct"] = shortfallByProduct,
                ["requestedQty"] = requestedQty,
            });
    }

    private static Guid ResolveActorId(Guid? accountId)
    {
        if (accountId.HasValue && accountId.Value != Guid.Empty)
        {
            return accountId.Value;
        }

        return Guid.Empty;
    }
}
