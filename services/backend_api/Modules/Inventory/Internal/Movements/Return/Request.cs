namespace BackendApi.Modules.Inventory.Internal.Movements.Return;

public sealed record ReturnMovementRequest(
    Guid OrderId,
    Guid? AccountId,
    string? ReasonCode,
    IReadOnlyList<ReturnMovementItem> Items);

public sealed record ReturnMovementItem(
    Guid ProductId,
    Guid WarehouseId,
    Guid? BatchId,
    int Qty);

public sealed record ReturnMovementResponse(Guid OrderId, IReadOnlyList<long> MovementIds);
