using MediatR;

namespace BackendApi.Modules.Shared;

/// <summary>
/// Spec 021 company-invitation lifecycle domain events (data-model §6). Consumed by spec
/// 025 (transactional invite emails) and spec 028 (audit reporting). State writes never
/// block on subscriber delivery (FR-043).
///
/// Per Constitution Principle 25, every event carries the actor and the timestamp at
/// which the lifecycle change was applied. Identity beyond the actor id (email, name)
/// is resolved against the spec 003 audit log when a downstream consumer needs it.
/// </summary>
public sealed record CompanyInvitationSent(
    Guid InvitationId,
    Guid CompanyId,
    string InvitedEmail,
    string TargetRole,
    string LocaleHint,
    Guid ActorId,
    DateTimeOffset PerformedAt) : INotification;

public sealed record CompanyInvitationAccepted(
    Guid InvitationId,
    Guid CompanyId,
    Guid InviteeUserId,
    string TargetRole,
    Guid ActorId,
    DateTimeOffset PerformedAt) : INotification;

public sealed record CompanyInvitationDeclined(
    Guid InvitationId,
    Guid CompanyId,
    string InvitedEmail,
    Guid ActorId,
    DateTimeOffset PerformedAt) : INotification;

public sealed record CompanyInvitationExpired(
    Guid InvitationId,
    Guid CompanyId,
    string InvitedEmail,
    Guid ActorId,
    DateTimeOffset PerformedAt) : INotification;
