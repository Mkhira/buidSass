namespace BackendApi.Modules.Search.Admin.ListJobs;

public sealed record ListJobsRequest(
    string? Index,
    string? Status,
    int? Page,
    int? PageSize);

public sealed record ListJobsResponse(
    IReadOnlyList<ListJobsItem> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record ListJobsItem(
    Guid JobId,
    string IndexName,
    string Status,
    Guid StartedByAccountId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? DocsExpected,
    int DocsWritten,
    string? Error);
