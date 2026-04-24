using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Inventory.Internal.Reservations.Convert;

public static class Handler
{
    public sealed record Result(
        bool IsSuccess,
        int StatusCode,
        string? ReasonCode,
        string? Detail,
        ConvertReservationResponse? Response,
        IDictionary<string, object?>? Extensions = null);

    public static async Task<Result> HandleAsync(
        Guid reservationId,
        ConvertReservationRequest request,
        InventoryDbContext inventoryDb,
        AtsCalculator atsCalculator,
        BucketMapper bucketMapper,
        ReorderAlertEmitter reorderAlertEmitter,
        AvailabilityEventEmitter availabilityEventEmitter,
        IAuditEventPublisher auditEventPublisher,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (reservationId == Guid.Empty)
        {
            return new Result(false, 400, "inventory.reservation.not_found", "Reservation id is required.", null);
        }

        if (request.OrderId == Guid.Empty)
        {
            return new Result(false, 400, "inventory.invalid_order_id", "Order id is required.", null);
        }

        var nowUtc = DateTimeOffset.UtcNow;

        await using var tx = await inventoryDb.Database.BeginTransactionAsync(cancellationToken);

        var reservation = await inventoryDb.InventoryReservations
            .FromSqlInterpolated($"""
                SELECT *
                FROM inventory.inventory_reservations
                WHERE "Id" = {reservationId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (reservation is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return new Result(false, 404, "inventory.reservation.not_found", "Reservation was not found.", null);
        }

        if (string.Equals(reservation.Status, "converted", StringComparison.OrdinalIgnoreCase))
        {
            await tx.RollbackAsync(cancellationToken);
            return new Result(false, 409, "inventory.reservation.already_converted", "Reservation has already been converted.", null);
        }

        if (!string.Equals(reservation.Status, "active", StringComparison.OrdinalIgnoreCase) || reservation.ExpiresAt <= nowUtc)
        {
            await tx.RollbackAsync(cancellationToken);
            return new Result(false, 409, "inventory.reservation.expired", "Reservation is expired or no longer active.", null);
        }

        var stock = await inventoryDb.StockLevels
            .FromSqlInterpolated($"""
                SELECT *
                FROM inventory.stock_levels
                WHERE "ProductId" = {reservation.ProductId}
                  AND "WarehouseId" = {reservation.WarehouseId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (stock is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return new Result(false, 409, "inventory.insufficient", "Stock row is missing.", null);
        }

        if (stock.OnHand < reservation.Qty)
        {
            await tx.RollbackAsync(cancellationToken);
            return BuildInsufficient(reservation.ProductId, reservation.Qty - Math.Max(0, stock.OnHand));
        }

        InventoryBatch? batch = null;
        if (reservation.PickedBatchId.HasValue)
        {
            batch = await inventoryDb.InventoryBatches
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM inventory.inventory_batches
                    WHERE "Id" = {reservation.PickedBatchId.Value}
                      AND "ProductId" = {reservation.ProductId}
                      AND "WarehouseId" = {reservation.WarehouseId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);

            // If the pinned batch was adjusted / written off / reassigned between reservation and
            // convert, reject rather than silently clamp the decrement below. Preserves the
            // invariant `stock.OnHand == sum(active_batches.QtyOnHand)`.
            if (batch is null || batch.QtyOnHand < reservation.Qty)
            {
                await tx.RollbackAsync(cancellationToken);
                return BuildInsufficient(reservation.ProductId, reservation.Qty);
            }
        }

        var before = new
        {
            reservation.Status,
            reservation.ConvertedAt,
            reservation.OrderId,
            stock.OnHand,
            stock.Reserved,
            stock.BucketCache,
            BatchQtyOnHand = batch?.QtyOnHand,
        };

        var atsBefore = atsCalculator.Compute(stock.OnHand, stock.Reserved, stock.SafetyStock);
        stock.OnHand -= reservation.Qty;
        stock.Reserved = Math.Max(0, stock.Reserved - reservation.Qty);
        stock.UpdatedAt = nowUtc;
        var atsAfter = atsCalculator.Compute(stock.OnHand, stock.Reserved, stock.SafetyStock);
        stock.BucketCache = bucketMapper.Map(atsAfter);

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
            before.BucketCache,
            stock.BucketCache,
            nowUtc,
            cancellationToken);

        if (batch is not null)
        {
            // Validated above to have at least reservation.Qty — decrement directly (no clamp).
            batch.QtyOnHand -= reservation.Qty;
            if (batch.QtyOnHand == 0)
            {
                batch.Status = "depleted";
            }
        }

        reservation.Status = "converted";
        reservation.ConvertedAt = nowUtc;
        reservation.OrderId = request.OrderId;

        var movement = new InventoryMovement
        {
            ProductId = reservation.ProductId,
            WarehouseId = reservation.WarehouseId,
            MarketCode = reservation.MarketCode,
            BatchId = reservation.PickedBatchId,
            Kind = "sale",
            Delta = -reservation.Qty,
            Reason = "inventory.reservation.convert",
            SourceKind = "order",
            SourceId = request.OrderId,
            ActorAccountId = request.AccountId,
            OccurredAt = nowUtc,
        };

        inventoryDb.InventoryMovements.Add(movement);

        await inventoryDb.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var actorId = request.AccountId.GetValueOrDefault();
        if (actorId != Guid.Empty)
        {
            await auditEventPublisher.PublishAsync(new AuditEvent(
                actorId,
                "internal",
                "inventory.reservation.converted",
                nameof(InventoryReservation),
                reservation.Id,
                before,
                new
                {
                    reservation.Status,
                    reservation.ConvertedAt,
                    reservation.OrderId,
                    stock.OnHand,
                    stock.Reserved,
                    stock.BucketCache,
                    BatchQtyOnHand = batch?.QtyOnHand,
                    MovementId = movement.Id,
                },
                "inventory.reservation.convert"), cancellationToken);
        }

        logger.LogInformation(
            "inventory.reservation.convert warehouseId={WarehouseId} productId={ProductId} qty={Qty} reservationId={ReservationId} movementId={MovementId}",
            reservation.WarehouseId,
            reservation.ProductId,
            reservation.Qty,
            reservation.Id,
            movement.Id);

        return new Result(true, 200, null, null, new ConvertReservationResponse(reservation.Id, movement.Id));
    }

    private static Result BuildInsufficient(Guid productId, int shortfall)
    {
        var shortfallByProduct = new Dictionary<string, int>
        {
            [productId.ToString("D")] = shortfall,
        };

        return new Result(
            false,
            StatusCodes.Status409Conflict,
            "inventory.insufficient",
            "Requested quantity exceeds on-hand stock during conversion.",
            null,
            new Dictionary<string, object?>
            {
                ["shortfallByProduct"] = shortfallByProduct,
            });
    }
}
