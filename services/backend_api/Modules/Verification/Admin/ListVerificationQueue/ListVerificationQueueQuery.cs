namespace BackendApi.Modules.Verification.Admin.ListVerificationQueue;

/// <summary>Query params per spec 020 contracts §3.1.</summary>
public sealed record ListVerificationQueueQuery(
    string? MarketFilter,
    IReadOnlyCollection<string>? StateFilter,
    IReadOnlyCollection<string>? ProfessionFilter,
    int? AgeMinBusinessDays,
    string? Search,
    string Sort,           // "oldest" | "newest"
    int Page,
    int PageSize);

/// <summary>One row in the reviewer's queue per contracts §3.1 success shape.</summary>
public sealed record ListVerificationQueueRow(
    Guid Id,
    string State,
    string MarketCode,
    string Profession,
    DateTimeOffset SubmittedAt,
    string SlaSignal,      // "ok" | "warning" | "breach"
    int AgeBusinessDays);

public sealed record ListVerificationQueueResponse(
    IReadOnlyList<ListVerificationQueueRow> Items,
    int Page,
    int PageSize,
    int TotalCount);
