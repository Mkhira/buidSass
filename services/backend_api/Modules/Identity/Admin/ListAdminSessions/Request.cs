namespace BackendApi.Modules.Identity.Admin.ListAdminSessions;

public sealed record ListAdminSessionsRequest(Guid AccountId);

public sealed record ListAdminSessionsResponse(IReadOnlyCollection<AdminSessionResponseItem> Sessions);

public sealed record AdminSessionResponseItem(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    string? ClientAgent,
    bool IsCurrent);
