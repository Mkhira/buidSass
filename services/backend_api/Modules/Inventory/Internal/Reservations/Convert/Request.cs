namespace BackendApi.Modules.Inventory.Internal.Reservations.Convert;

public sealed record ConvertReservationRequest(Guid OrderId, Guid? AccountId);

public sealed record ConvertReservationResponse(Guid ReservationId, long MovementId);
