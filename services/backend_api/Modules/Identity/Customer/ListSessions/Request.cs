namespace BackendApi.Modules.Identity.Customer.ListSessions;

public sealed record ListSessionsRequest;

public sealed record ListSessionsResponse(IReadOnlyCollection<CustomerSessionResponseItem> Sessions);

public sealed record CustomerSessionResponseItem(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    string? ClientAgent,
    bool IsCurrent);
