namespace BackendApi.Modules.Support.Lead.ReassignTicket;

/// <summary>
/// Lead-initiated reassignment per spec 023 US4 + FR-014a + FR-039.
/// </summary>
/// <param name="TicketId">The ticket being reassigned.</param>
/// <param name="LeadActorId">The lead performing the reassignment (audit only).</param>
/// <param name="TargetAgentId">The agent receiving the ticket.</param>
/// <param name="JustificationNote">
/// FR-008-style mandatory note (≥ 10 chars) capturing the operational rationale.
/// </param>
public sealed record ReassignTicketCommand(
    Guid TicketId,
    Guid LeadActorId,
    Guid TargetAgentId,
    string JustificationNote);

public sealed record ReassignTicketResult(
    bool Success,
    Guid? AssignmentId,
    Guid? PriorAgentId,
    string? ReasonCode,
    string? Detail);
