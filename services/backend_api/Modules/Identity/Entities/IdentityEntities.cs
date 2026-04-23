namespace BackendApi.Modules.Identity.Entities;

public sealed class Account
{
    public Guid Id { get; set; }
    public string Surface { get; set; } = string.Empty;
    public string MarketCode { get; set; } = string.Empty;
    public string EmailNormalized { get; set; } = string.Empty;
    public string EmailDisplay { get; set; } = string.Empty;
    public string? PhoneE164 { get; set; }
    public string? PhoneMarketCode { get; set; }
    public string ProfessionalVerificationStatus { get; set; } = "unverified";
    public DateTimeOffset? ProfessionalVerificationStatusUpdatedAt { get; set; }
    public Guid? CompanyAccountId { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public short PasswordHashVersion { get; set; } = 1;
    public int PermissionVersion { get; set; } = 1;
    public string Status { get; set; } = "pending_email_verification";
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public DateTimeOffset? PhoneVerifiedAt { get; set; }
    public string Locale { get; set; } = "ar";
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class Session
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Surface { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ClientAgent { get; set; }
    public byte[] ClientIpHash { get; set; } = [];
    public byte[]? ClientFingerprintHash { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
}

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid? TokenId { get; set; }
    public byte[]? TokenSecretHash { get; set; }
    public byte[]? TokenHash { get; set; }
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset? ConsumedAt { get; set; }
    public Guid? SupersededBy { get; set; }
}

public sealed class RevokedRefreshToken
{
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset RevokedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Reason { get; set; } = string.Empty;
    public Guid? ActorId { get; set; }
}

public sealed class OtpChallenge
{
    public Guid Id { get; set; }
    public Guid? AccountId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string Surface { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public byte[] DestinationHash { get; set; } = [];
    public byte[] CodeHash { get; set; } = [];
    public short CodeLength { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public short MaxAttempts { get; set; } = 3;
    public short Attempts { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class EmailVerificationChallenge
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid? TokenId { get; set; }
    public byte[]? TokenSecretHash { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid? TokenId { get; set; }
    public byte[]? TokenSecretHash { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class AdminInvitation
{
    public Guid Id { get; set; }
    public string EmailNormalized { get; set; } = string.Empty;
    public Guid InvitedByAccountId { get; set; }
    public Guid InvitedRoleId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "pending";
    public Guid? AcceptedAccountId { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}

public sealed class AdminMfaFactor
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Kind { get; set; } = "totp";
    public byte[] SecretEncrypted { get; set; } = [];
    public byte[]? ProvisioningTokenHash { get; set; }
    public DateTimeOffset? ProvisioningTokenExpiresAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public string RecoveryCodesHash { get; set; } = "[]";
}

public sealed class AdminMfaReplayGuard
{
    public Guid FactorId { get; set; }
    public long WindowCounter { get; set; }
    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AdminPartialAuthToken
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public byte[] TokenSecretHash { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed class AdminMfaChallenge
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid FactorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? ExhaustedAt { get; set; }
    public short Attempts { get; set; }
    public short MaxAttempts { get; set; } = 3;
}

public sealed class LockoutState
{
    public Guid AccountId { get; set; }
    public string Reason { get; set; } = "signin";
    public int FailedCount { get; set; }
    public int Tier { get; set; }
    public int CooldownIndex { get; set; }
    public bool RequiresAdminUnlock { get; set; }
    public DateTimeOffset? FirstFailedAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Role
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Scope { get; set; } = "platform";
    public bool System { get; set; }
}

public sealed class Permission
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}

public sealed class AccountRole
{
    public Guid AccountId { get; set; }
    public Guid RoleId { get; set; }
    public string MarketCode { get; set; } = "platform";
    public Guid? GrantedByAccountId { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuthorizationAudit
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? AccountId { get; set; }
    public string Surface { get; set; } = string.Empty;
    public string PermissionCode { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
}

public sealed class RateLimitEvent
{
    public Guid Id { get; set; }
    public string PolicyCode { get; set; } = string.Empty;
    public byte[] ScopeKeyHash { get; set; } = [];
    public DateTimeOffset BlockedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Surface { get; set; } = string.Empty;
}
