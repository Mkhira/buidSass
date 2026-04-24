namespace BackendApi.Modules.Inventory.Internal.Reservations.Release;

public sealed record ReleaseReservationRequest(Guid? AccountId, string? Reason);
