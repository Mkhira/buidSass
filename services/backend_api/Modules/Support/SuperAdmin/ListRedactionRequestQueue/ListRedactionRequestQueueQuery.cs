namespace BackendApi.Modules.Support.SuperAdmin.ListRedactionRequestQueue;

public sealed record ListRedactionRequestQueueQuery(
    string? MarketCode,
    int Page = 1,
    int PageSize = 50);

public sealed record RedactionRequestQueueRow(
    Guid TicketId,
    Guid CustomerId,
    string MarketCode,
    string State,
    string Subject,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset FirstResponseDueUtc,
    DateTimeOffset? BreachAcknowledgedAtFirstResponse);

public sealed record ListRedactionRequestQueueResult(
    IReadOnlyList<RedactionRequestQueueRow> Rows,
    int Page,
    int PageSize,
    int TotalCount);
