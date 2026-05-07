using MediatR;

namespace BackendApi.Modules.Shared;

/// <summary>
/// 16 in-process MediatR domain events emitted by spec 023 per data-model §6.
/// Spec 025 (notifications) subscribes; 023 only emits.
/// </summary>
public sealed record TicketOpened(
    Guid TicketId,
    Guid CustomerId,
    string MarketCode,
    string Category,
    string Priority,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record TicketAssigned(
    Guid TicketId,
    Guid AgentId,
    string AssignmentKind,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record TicketReassigned(
    Guid TicketId,
    Guid? PriorAgentId,
    Guid NewAgentId,
    string JustificationNote,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record TicketCustomerReplyReceived(
    Guid TicketId,
    Guid MessageId,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record TicketAgentReplySent(
    Guid TicketId,
    Guid MessageId,
    bool LeadIntervention,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record TicketStateChanged(
    Guid TicketId,
    string FromState,
    string ToState,
    string TriggeredBy,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record TicketResolved(
    Guid TicketId,
    Guid? ResolvedByAgentId,
    DateTimeOffset ResolvedAtUtc) : INotification;

public sealed record TicketClosed(
    Guid TicketId,
    string TriggeredBy,
    DateTimeOffset ClosedAtUtc) : INotification;

public sealed record TicketReopened(
    Guid TicketId,
    int ReopenCount,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record TicketSlaBreachedFirstResponse(
    Guid TicketId,
    DateTimeOffset TargetDueUtc,
    Guid? AgentId,
    DateTimeOffset DetectedAtUtc) : INotification;

public sealed record TicketSlaBreachedResolution(
    Guid TicketId,
    DateTimeOffset TargetDueUtc,
    Guid? AgentId,
    DateTimeOffset DetectedAtUtc) : INotification;

public sealed record TicketConvertedToReturn(
    Guid TicketId,
    Guid ReturnRequestId,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record TicketReturnOutcomeReceived(
    Guid TicketId,
    Guid ReturnRequestId,
    string Outcome,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record TicketAttachmentRedacted(
    Guid TicketId,
    Guid AttachmentId,
    Guid RequestingActorId,
    DateTimeOffset OccurredAtUtc) : INotification;

/// <summary>
/// Spec 023 FR-011a — emitted on super-admin message-body redaction. Carries
/// both the requesting customer (whose redaction-request ticket triggered
/// this) AND the super-admin actor who actually performed the wipe, so the
/// audit-log consumer can record full actor accountability per Principle 25.
/// </summary>
public sealed record TicketMessageRedacted(
    Guid TicketId,
    Guid MessageId,
    Guid RequestingCustomerId,
    Guid SuperAdminActorId,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record TicketAgentAvailabilityChanged(
    Guid AgentId,
    string MarketCode,
    bool IsOnCall,
    DateTimeOffset OccurredAtUtc) : INotification;

/// <summary>
/// Spec 023 FR-026 — emitted when a lead overrides the per-ticket SLA targets.
/// Carries actor + justification + old/new targets so spec 025 (notifications)
/// and the audit-log consumer can record the change without repurposing the
/// <see cref="TicketStateChanged"/> catch-all event for a non-transition.
/// </summary>
public sealed record TicketSlaOverridden(
    Guid TicketId,
    Guid LeadActorId,
    int PriorFirstResponseTargetMinutes,
    int PriorResolutionTargetMinutes,
    int NewFirstResponseTargetMinutes,
    int NewResolutionTargetMinutes,
    DateTimeOffset NewFirstResponseDueUtc,
    DateTimeOffset NewResolutionDueUtc,
    string JustificationNote,
    DateTimeOffset OccurredAtUtc) : INotification;
