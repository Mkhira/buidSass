using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Inventory.Internal.Movements.Return;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using BackendApi.Modules.Shared;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Inventory.Internal.Reservations.Convert;

/// <summary>
/// CR review round 2 (Major) — adapter that lets Orders call the inventory conversion + return
/// handlers via <see cref="IReservationConverter"/> instead of taking a static dependency on
/// the per-slice handler types. Registered in <c>InventoryModule</c>.
/// </summary>
public sealed class InventoryReservationConverter(
    InventoryDbContext inventoryDb,
    AtsCalculator atsCalculator,
    BucketMapper bucketMapper,
    ReorderAlertEmitter reorderAlertEmitter,
    AvailabilityEventEmitter availabilityEventEmitter,
    IAuditEventPublisher auditEventPublisher,
    ILogger<InventoryReservationConverter> logger) : IReservationConverter
{
    public async Task<ReservationConversionResult> ConvertAsync(
        Guid reservationId,
        Guid orderId,
        Guid? actorAccountId,
        CancellationToken cancellationToken)
    {
        var result = await Handler.HandleAsync(
            reservationId,
            new ConvertReservationRequest(orderId, actorAccountId),
            inventoryDb,
            atsCalculator,
            bucketMapper,
            reorderAlertEmitter,
            availabilityEventEmitter,
            auditEventPublisher,
            logger,
            cancellationToken);
        return new ReservationConversionResult(
            IsSuccess: result.IsSuccess,
            ReasonCode: result.ReasonCode,
            Detail: result.Detail,
            MovementId: result.Response?.MovementId);
    }

    public async Task<ReservationReturnResult> PostReturnAsync(
        Guid orderId,
        Guid actorAccountId,
        IReadOnlyList<ReservationReturnLine> items,
        string? reasonCode,
        CancellationToken cancellationToken)
    {
        var mapped = items.Select(i => new ReturnMovementItem(
            ProductId: i.ProductId,
            WarehouseId: i.WarehouseId,
            BatchId: i.BatchId,
            Qty: i.Qty)).ToArray();
        var result = await BackendApi.Modules.Inventory.Internal.Movements.Return.Handler.HandleAsync(
            new ReturnMovementRequest(orderId, actorAccountId, reasonCode, mapped),
            inventoryDb,
            atsCalculator,
            bucketMapper,
            availabilityEventEmitter,
            auditEventPublisher,
            actorAccountId,
            cancellationToken);
        return new ReservationReturnResult(
            IsSuccess: result.IsSuccess,
            ReasonCode: result.ReasonCode,
            MovementIds: result.Response?.MovementIds ?? Array.Empty<long>());
    }
}
