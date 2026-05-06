namespace BackendApi.Modules.Support.Customer.OpenTicket;

/// <summary>
/// Customer-side ticket creation command per quickstart §1.4 / spec 023 US1.
/// </summary>
public sealed record OpenTicketCommand(
    Guid CustomerId,
    string CustomerMarketCode,
    string Locale,
    string Category,
    string Priority,
    string Subject,
    string Body,
    string? LinkedEntityKind,
    Guid? LinkedEntityId,
    IReadOnlyList<Guid>? AttachmentIds);

public sealed record OpenTicketResult(
    bool Success,
    Guid? TicketId,
    string? State,
    string? MarketCode,
    Guid? AssignedAgentId,
    DateTimeOffset? FirstResponseDueUtc,
    DateTimeOffset? ResolutionDueUtc,
    string? ReasonCode,
    string? Detail);
