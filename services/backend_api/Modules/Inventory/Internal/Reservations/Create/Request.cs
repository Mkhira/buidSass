namespace BackendApi.Modules.Inventory.Internal.Reservations.Create;

public sealed record CreateReservationRequest(
    Guid? CartId,
    Guid? AccountId,
    string MarketCode,
    IReadOnlyList<CreateReservationItem> Items);

public sealed record CreateReservationItem(Guid ProductId, int Qty);

public sealed record CreateReservationResponse(Guid ReservationId, IReadOnlyList<CreateReservationItemResponse> Items);

public sealed record CreateReservationItemResponse(Guid ProductId, int Qty, Guid PickedBatchId, DateTimeOffset ExpiresAt);
