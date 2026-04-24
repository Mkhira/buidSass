using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Inventory.Internal.Reservations.Release;

public static class Handler
{
    public sealed record Result(bool IsSuccess, int StatusCode, string? ReasonCode, string? Detail);

    public static async Task<Result> HandleAsync(
        Guid reservationId,
        Guid actorId,
        string? reason,
        InventoryDbContext inventoryDb,
        AtsCalculator atsCalculator,
        BucketMapper bucketMapper,
        AvailabilityEventEmitter availabilityEventEmitter,
        IAuditEventPublisher auditEventPublisher,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (reservationId == Guid.Empty)
        {
            return new Result(false, 400, "inventory.reservation.not_found", "Reservation id is required.");
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
            return new Result(false, 404, "inventory.reservation.not_found", "Reservation was not found.");
        }

        if (string.Equals(reservation.Status, "converted", StringComparison.OrdinalIgnoreCase))
        {
            await tx.RollbackAsync(cancellationToken);
            return new Result(false, 409, "inventory.reservation.already_converted", "Reservation has already been converted.");
        }

        if (string.Equals(reservation.Status, "released", StringComparison.OrdinalIgnoreCase))
        {
            await tx.RollbackAsync(cancellationToken);
            return new Result(true, 204, null, null);
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
            return new Result(false, 409, "inventory.insufficient", "Stock row does not exist for this reservation.");
        }

        var before = new
        {
            reservation.Status,
            reservation.ReleasedAt,
            stock.Reserved,
            stock.BucketCache,
        };

        var bucketBefore = stock.BucketCache;
        stock.Reserved = Math.Max(0, stock.Reserved - reservation.Qty);
        stock.UpdatedAt = nowUtc;
        stock.BucketCache = bucketMapper.Map(atsCalculator.Compute(stock.OnHand, stock.Reserved, stock.SafetyStock));

        await availabilityEventEmitter.EmitIfChangedAsync(
            stock.ProductId,
            stock.WarehouseId,
            bucketBefore,
            stock.BucketCache,
            nowUtc,
            cancellationToken);

        reservation.Status = "released";
        reservation.ReleasedAt = nowUtc;

        await inventoryDb.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        if (actorId != Guid.Empty)
        {
            await auditEventPublisher.PublishAsync(new AuditEvent(
                actorId,
                "internal",
                "inventory.reservation.released",
                nameof(InventoryReservation),
                reservation.Id,
                before,
                new
                {
                    reservation.Status,
                    reservation.ReleasedAt,
                    stock.Reserved,
                    stock.BucketCache,
                },
                reason ?? "inventory.reservation.release"), cancellationToken);
        }

        logger.LogInformation(
            "inventory.reservation.release warehouseId={WarehouseId} productId={ProductId} qty={Qty} reservationId={ReservationId}",
            reservation.WarehouseId,
            reservation.ProductId,
            reservation.Qty,
            reservation.Id);

        return new Result(true, 204, null, null);
    }
}
