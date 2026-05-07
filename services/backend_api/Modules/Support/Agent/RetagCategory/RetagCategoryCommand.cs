namespace BackendApi.Modules.Support.Agent.RetagCategory;

/// <summary>
/// Spec 023 T124 — agent-side ticket-category retagging. Validates the
/// new category against the existing linked-entity kind per FR-007 (reuses
/// <c>TicketCategoryNames.IsConsistentWithLinkedKind</c>) and audits the
/// change via a <c>system_event</c> message in the thread. Rate-limit
/// middleware (cross-spec) will gate the endpoint once spec 004's pipeline
/// finalises; the handler itself is rate-limit-aware via the standard
/// per-actor admin throttles.
/// </summary>
public sealed record RetagCategoryCommand(
    Guid TicketId,
    Guid ActorId,
    string ActorRole,
    string NewCategory,
    string? Justification);

public sealed record RetagCategoryResult(
    bool Success,
    string? PriorCategory,
    string? NewCategory,
    string? ReasonCode,
    string? Detail);
