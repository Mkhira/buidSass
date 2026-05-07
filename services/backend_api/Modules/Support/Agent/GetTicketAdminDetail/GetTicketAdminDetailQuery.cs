namespace BackendApi.Modules.Support.Agent.GetTicketAdminDetail;

/// <summary>
/// Spec 023 T071 + T136c — admin-side full ticket-detail read. Returns the
/// complete thread (including <c>internal_note</c> rows when the actor is
/// support_agent / support_lead / super_admin per
/// <c>contracts/support-tickets-contract.md §2</c>), the full assignment
/// history, the ticket meta-block including SLA snapshot + breach
/// acknowledgments, and a per-kind linked-entity preview when present.
///
/// <para>FR-016a customer-display rule (T136c): the ticket-author display
/// follows the canonical reviewer-display rule via
/// <c>IReviewDisplayHandleQuery</c>; for B2B customers the company name
/// is surfaced as a secondary line via <c>ICompanyAccountQuery</c>.</para>
/// </summary>
public sealed record GetTicketAdminDetailQuery(
    Guid TicketId,
    Guid ActorId,
    bool IsLeadOrSuperAdmin,
    string MarketCode,
    bool IsSuperAdmin);

public sealed record AdminTicketMessageDto(
    Guid Id,
    string Kind,
    Guid? ActorId,
    string ActorRole,
    string? Body,
    string? BodyLocale,
    bool LeadIntervention,
    DateTimeOffset? RedactedAtUtc,
    string? RedactedByRole,
    DateTimeOffset CreatedAtUtc);

public sealed record AdminTicketAssignmentDto(
    Guid Id,
    Guid AgentId,
    string AssignmentKind,
    Guid? AssignedByActorId,
    string? JustificationNote,
    DateTimeOffset AssignedAtUtc,
    DateTimeOffset? SupersededAtUtc,
    string? SupersededReason);

public sealed record AdminTicketLinkDto(
    Guid Id,
    string Kind,
    Guid LinkedEntityId,
    string CreatedVia,
    DateTimeOffset CreatedAtUtc);

public sealed record AdminTicketCustomerDisplay(
    Guid CustomerId,
    string DisplayName,
    string? CompanyName);

public sealed record AdminTicketLinkedEntityPreview(
    string Kind,
    Guid LinkedEntityId,
    string MarketCode,
    Guid? VendorId,
    string? DisplaySummary);

public sealed record GetTicketAdminDetailResult(
    bool Success,
    string? ReasonCode,
    string? Detail,
    Guid? TicketId = null,
    string? State = null,
    string? Category = null,
    string? Priority = null,
    string? Subject = null,
    string? Body = null,
    string? MarketCode = null,
    string? Locale = null,
    Guid? AssignedAgentId = null,
    DateTimeOffset? CreatedAtUtc = null,
    DateTimeOffset? UpdatedAtUtc = null,
    DateTimeOffset? ResolvedAtUtc = null,
    DateTimeOffset? ClosedAtUtc = null,
    int? ReopenCount = null,
    int? FirstResponseTargetMinutesSnapshot = null,
    int? ResolutionTargetMinutesSnapshot = null,
    DateTimeOffset? FirstResponseDueUtc = null,
    DateTimeOffset? ResolutionDueUtc = null,
    DateTimeOffset? BreachAcknowledgedAtFirstResponse = null,
    DateTimeOffset? BreachAcknowledgedAtResolution = null,
    AdminTicketCustomerDisplay? Customer = null,
    AdminTicketLinkedEntityPreview? LinkedEntityPreview = null,
    IReadOnlyList<AdminTicketMessageDto>? Messages = null,
    IReadOnlyList<AdminTicketAssignmentDto>? Assignments = null,
    IReadOnlyList<AdminTicketLinkDto>? Links = null);
