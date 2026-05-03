using MediatR;

namespace BackendApi.Modules.Shared;

/// <summary>
/// Spec 021 company-invitation lifecycle domain events (data-model §6). Consumed by spec
/// 025 (transactional invite emails) and spec 028 (audit reporting). State writes never
/// block on subscriber delivery (FR-043).
/// </summary>
public sealed record CompanyInvitationSent(
    Guid InvitationId,
    Guid CompanyId,
    string InvitedEmail,
    string TargetRole,
    string LocaleHint) : INotification;

public sealed record CompanyInvitationAccepted(
    Guid InvitationId,
    Guid CompanyId,
    Guid InviteeUserId,
    string TargetRole) : INotification;

public sealed record CompanyInvitationDeclined(
    Guid InvitationId,
    Guid CompanyId,
    string InvitedEmail) : INotification;

public sealed record CompanyInvitationExpired(
    Guid InvitationId,
    Guid CompanyId,
    string InvitedEmail) : INotification;
