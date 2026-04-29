namespace BackendApi.Modules.Verification.Customer.ListMyVerifications;

public sealed record ListMyVerificationsQuery(int Page, int PageSize);

public sealed record ListMyVerificationsRow(
    Guid Id,
    string State,
    string MarketCode,
    string Profession,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? ExpiresAt);

public sealed record ListMyVerificationsResponse(
    IReadOnlyList<ListMyVerificationsRow> Items,
    int Page,
    int PageSize,
    int TotalCount);
