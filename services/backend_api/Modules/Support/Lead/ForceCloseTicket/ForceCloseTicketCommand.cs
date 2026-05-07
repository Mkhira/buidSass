namespace BackendApi.Modules.Support.Lead.ForceCloseTicket;

public sealed record ForceCloseTicketCommand(
    Guid TicketId,
    Guid LeadActorId,
    string ReasonNote);

public sealed record ForceCloseTicketResult(
    bool Success,
    string? PriorState,
    DateTimeOffset? ClosedAtUtc,
    string? ReasonCode,
    string? Detail);
