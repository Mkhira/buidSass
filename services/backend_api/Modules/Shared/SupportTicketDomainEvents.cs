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

public sealed record TicketMessageRedacted(
    Guid TicketId,
    Guid MessageId,
    Guid RequestingCustomerId,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record TicketAgentAvailabilityChanged(
    Guid AgentId,
    string MarketCode,
    bool IsOnCall,
    DateTimeOffset OccurredAtUtc) : INotification;
