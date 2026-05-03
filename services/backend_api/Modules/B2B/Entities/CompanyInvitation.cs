namespace BackendApi.Modules.B2B.Entities;

/// <summary>
/// Spec 021 company invitation (data-model §2.4). Opaque token + 14-day TTL (per market).
/// Lifecycle managed by <see cref="Primitives.CompanyInvitationStateMachine"/>.
///
/// The plaintext token is never persisted: only its HMAC-SHA256 digest is stored in
/// <see cref="TokenHash"/>. Plaintext lives only in memory long enough to be embedded
/// in the invitation email/notification, then is discarded. Acceptance hashes the
/// supplied plaintext with the same key and compares the digests.
/// </summary>
public sealed class CompanyInvitation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>The <c>companies.admin</c> user who issued the invitation.</summary>
    public Guid InvitedBy { get; set; }

    /// <summary>Lower-cased normalized email; CITEXT in storage.</summary>
    public string InvitedEmail { get; set; } = string.Empty;

    /// <summary>Target membership role: <c>companies.admin | buyer | approver</c>.</summary>
    public string TargetRole { get; set; } = string.Empty;

    /// <summary>
    /// Hex-encoded HMAC-SHA256 digest of the 32-byte URL-safe random plaintext token,
    /// keyed by <c>B2BInvitationOptions.SigningKey</c>. UNIQUE; lookups go through
    /// <c>CompanyInvitationTokenHasher.Hash</c>. The plaintext is never stored.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>One of <see cref="Primitives.CompanyInvitationState"/>'s tokens.</summary>
    public string State { get; set; } = "pending";

    public DateTimeOffset SentAt { get; set; }

    /// <summary>Anchored to <c>SentAt + market_schema.invitation_ttl_days</c>.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
