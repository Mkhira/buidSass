namespace BackendApi.Modules.Shared;

/// <summary>
/// Bridge between Orders (spec 011) and Inventory (spec 008) for the order-create flow.
/// Spec 011's <c>CreateFromCheckoutHandler</c> needs to convert reservations and (on cancel)
/// post return movements without taking a direct dependency on Inventory's internal Handlers
/// — that would break the vertical-slice boundary and bypass DI. Lives in
/// <c>Modules/Shared/</c> alongside <see cref="IOrderFromCheckoutHandler"/> and
/// <see cref="IOrderPaymentStateHook"/>.
/// </summary>
public interface IReservationConverter
{
    Task<ReservationConversionResult> ConvertAsync(
        Guid reservationId,
        Guid orderId,
        Guid? actorAccountId,
        CancellationToken cancellationToken);

    Task<ReservationReturnResult> PostReturnAsync(
        Guid orderId,
        Guid actorAccountId,
        IReadOnlyList<ReservationReturnLine> items,
        string? reasonCode,
        CancellationToken cancellationToken);
}

public sealed record ReservationConversionResult(
    bool IsSuccess,
    string? ReasonCode,
    string? Detail,
    long? MovementId);

public sealed record ReservationReturnLine(Guid ProductId, Guid WarehouseId, Guid? BatchId, int Qty);

public sealed record ReservationReturnResult(
    bool IsSuccess,
    string? ReasonCode,
    IReadOnlyList<long> MovementIds);
