namespace BackendApi.Modules.Support.Agent.ListTicketsByCustomer;

/// <summary>
/// Spec 023 T125 — admin-side support-investigation read. Mirrors the
/// shape of <c>ListMyTicketsQuery</c> but addresses tickets by an
/// arbitrary customer id (the agent investigates on behalf of the
/// customer). Permission-gated at the endpoint.
/// </summary>
public sealed record ListTicketsByCustomerQuery(
    Guid CustomerId,
    int Skip,
    int Take,
    string? StateFilter);

public sealed record AdminCustomerTicketListItem(
    Guid Id,
    string State,
    string Category,
    string Priority,
    string Subject,
    string MarketCode,
    Guid? AssignedAgentId,
    DateTimeOffset FirstResponseDueUtc,
    DateTimeOffset ResolutionDueUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ListTicketsByCustomerResult(
    IReadOnlyList<AdminCustomerTicketListItem> Items,
    int Total);
