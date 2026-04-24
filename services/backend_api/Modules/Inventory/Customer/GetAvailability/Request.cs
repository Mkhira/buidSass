namespace BackendApi.Modules.Inventory.Customer.GetAvailability;

public sealed record GetAvailabilityRequest(IReadOnlyList<Guid> ProductIds, string MarketCode);

public sealed record GetAvailabilityResponse(IReadOnlyList<GetAvailabilityItem> Items);

public sealed record GetAvailabilityItem(Guid ProductId, string Bucket);
