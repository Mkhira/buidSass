namespace BackendApi.Modules.Support.Entities;

/// <summary>
/// Append-only assignment history row. The active assignment is the row whose
/// <see cref="SupersededAtUtc"/> is null (enforced by partial unique index).
/// </summary>
public sealed class TicketAssignment
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public Guid AgentId { get; set; }
    public string AssignmentKind { get; set; } = string.Empty;
    public Guid? AssignedByActorId { get; set; }
    public string? JustificationNote { get; set; }
    public DateTimeOffset AssignedAtUtc { get; set; }
    public DateTimeOffset? SupersededAtUtc { get; set; }
    public string? SupersededReason { get; set; }
}

public static class TicketAssignmentKind
{
    public const string SelfClaim = "self_claim";
    public const string AutoAssignment = "auto_assignment";
    public const string LeadReassignment = "lead_reassignment";
    public const string ReclaimAfterOffboard = "reclaim_after_offboard";
}

public static class TicketAssignmentSupersededReason
{
    public const string Reassigned = "reassigned";
    public const string ReclaimedOffboard = "reclaimed_offboard";
    public const string ReopenedBackToQueue = "reopened_back_to_queue";
}
