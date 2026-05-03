namespace BackendApi.Modules.B2B.Entities;

/// <summary>
/// Spec 021 company invitation (data-model §2.4). Opaque token + 14-day TTL (per market).
/// Lifecycle managed by <see cref="Primitives.CompanyInvitationStateMachine"/>.
/// </summary>
public sealed class CompanyInvitation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }

    /// <summary>The <c>companies.admin</c> user who issued the invitation.</summary>
    public Guid InvitedBy { get; set; }

    /// <summary>Lower-cased normalized email; CITEXT in storage.</summary>
    public string InvitedEmail { get; set; } = string.Empty;

    /// <summary>Target membership role: <c>companies.admin | buyer | approver</c>.</summary>
    public string TargetRole { get; set; } = string.Empty;

    /// <summary>32-byte URL-safe random token; UNIQUE.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>One of <see cref="Primitives.CompanyInvitationState"/>'s tokens.</summary>
    public string State { get; set; } = "pending";

    public DateTimeOffset SentAt { get; set; }

    /// <summary>Anchored to <c>SentAt + market_schema.invitation_ttl_days</c>.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
