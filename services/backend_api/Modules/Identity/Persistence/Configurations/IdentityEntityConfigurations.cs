using BackendApi.Modules.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Identity.Persistence.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Surface).HasColumnType("citext").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.EmailNormalized).HasColumnType("citext").IsRequired();
        builder.Property(x => x.EmailDisplay).IsRequired();
        builder.Property(x => x.PhoneMarketCode).HasColumnType("citext");
        builder.Property(x => x.ProfessionalVerificationStatus)
            .HasColumnType("citext")
            .HasDefaultValue("unverified")
            .IsRequired();
        builder.Property(x => x.PermissionVersion).HasDefaultValue(1).IsRequired();
        builder.Property(x => x.Status).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Locale).HasColumnType("citext").HasDefaultValue("ar").IsRequired();
        builder.HasIndex(x => new { x.Surface, x.EmailNormalized }).IsUnique().HasFilter("\"DeletedAt\" IS NULL");
        builder.HasIndex(x => new { x.Surface, x.PhoneE164 }).IsUnique().HasFilter("\"PhoneE164\" IS NOT NULL AND \"DeletedAt\" IS NULL");
        builder.HasIndex(x => new { x.MarketCode, x.CreatedAt });
        builder.HasIndex(x => x.CompanyAccountId);
        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Surface).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Status).HasColumnType("citext").IsRequired();
        builder.Property(x => x.ClientFingerprintHash).HasColumnType("bytea");
        builder.HasIndex(x => new { x.AccountId, x.Status, x.LastSeenAt });
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasColumnType("citext").IsRequired();
        builder.Property(x => x.TokenId).HasColumnType("uuid");
        builder.Property(x => x.TokenSecretHash).HasColumnType("bytea");
        builder.HasIndex(x => x.SessionId).HasFilter("\"Status\" = 'active'").IsUnique();
        builder.HasIndex(x => x.TokenId).IsUnique().HasFilter("\"TokenId\" IS NOT NULL");
    }
}

public sealed class RevokedRefreshTokenConfiguration : IEntityTypeConfiguration<RevokedRefreshToken>
{
    public void Configure(EntityTypeBuilder<RevokedRefreshToken> builder)
    {
        builder.ToTable("revoked_refresh_tokens", "identity");
        builder.HasKey(x => x.TokenHash);
        builder.Property(x => x.Reason).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => x.RevokedAt);
    }
}

public sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenges", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Purpose).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Surface).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Channel).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Status).HasColumnType("citext").IsRequired();
        builder.Property(x => x.MaxAttempts).HasDefaultValue((short)3);
        builder.HasIndex(x => new { x.AccountId, x.Purpose, x.CreatedAt });
    }
}

public sealed class EmailVerificationChallengeConfiguration : IEntityTypeConfiguration<EmailVerificationChallenge>
{
    public void Configure(EntityTypeBuilder<EmailVerificationChallenge> builder)
    {
        builder.ToTable("email_verification_challenges", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasColumnType("citext").IsRequired();
        builder.Property(x => x.TokenId).HasColumnType("uuid");
        builder.Property(x => x.TokenSecretHash).HasColumnType("bytea");
        builder.HasIndex(x => x.TokenId).IsUnique().HasFilter("\"TokenId\" IS NOT NULL");
    }
}

public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasColumnType("citext").IsRequired();
        builder.Property(x => x.TokenId).HasColumnType("uuid");
        builder.Property(x => x.TokenSecretHash).HasColumnType("bytea");
        builder.HasIndex(x => x.TokenId).IsUnique().HasFilter("\"TokenId\" IS NOT NULL");
    }
}

public sealed class AdminInvitationConfiguration : IEntityTypeConfiguration<AdminInvitation>
{
    public void Configure(EntityTypeBuilder<AdminInvitation> builder)
    {
        builder.ToTable("admin_invitations", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EmailNormalized).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Status).HasColumnType("citext").IsRequired();
    }
}

public sealed class AdminMfaFactorConfiguration : IEntityTypeConfiguration<AdminMfaFactor>
{
    public void Configure(EntityTypeBuilder<AdminMfaFactor> builder)
    {
        builder.ToTable("admin_mfa_factors", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasColumnType("citext").IsRequired();
        builder.Property(x => x.RecoveryCodesHash).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.AccountId, x.Kind }).IsUnique().HasFilter("\"RevokedAt\" IS NULL");
    }
}

public sealed class AdminMfaReplayGuardConfiguration : IEntityTypeConfiguration<AdminMfaReplayGuard>
{
    public void Configure(EntityTypeBuilder<AdminMfaReplayGuard> builder)
    {
        builder.ToTable("admin_mfa_replay_guard", "identity");
        builder.HasKey(x => new { x.FactorId, x.WindowCounter });
        builder.HasIndex(x => x.ObservedAt);
    }
}

public sealed class AdminPartialAuthTokenConfiguration : IEntityTypeConfiguration<AdminPartialAuthToken>
{
    public void Configure(EntityTypeBuilder<AdminPartialAuthToken> builder)
    {
        builder.ToTable("admin_partial_auth_tokens", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenSecretHash).HasColumnType("bytea").IsRequired();
        builder.HasIndex(x => new { x.AccountId, x.ExpiresAt });
        builder.HasIndex(x => x.ConsumedAt);
    }
}

public sealed class AdminMfaChallengeConfiguration : IEntityTypeConfiguration<AdminMfaChallenge>
{
    public void Configure(EntityTypeBuilder<AdminMfaChallenge> builder)
    {
        builder.ToTable("admin_mfa_challenges", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Attempts).HasDefaultValue((short)0);
        builder.Property(x => x.MaxAttempts).HasDefaultValue((short)3);
        builder.HasIndex(x => new { x.AccountId, x.ExpiresAt });
        builder.HasIndex(x => x.FactorId);
        builder.HasIndex(x => x.ConsumedAt);
        builder.HasIndex(x => x.ExhaustedAt);
    }
}

public sealed class LockoutStateConfiguration : IEntityTypeConfiguration<LockoutState>
{
    public void Configure(EntityTypeBuilder<LockoutState> builder)
    {
        builder.ToTable("lockout_state", "identity");
        builder.HasKey(x => new { x.AccountId, x.Reason });
        builder.Property(x => x.Reason).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Tier).HasDefaultValue(0);
        builder.Property(x => x.CooldownIndex).HasDefaultValue(0);
        builder.Property(x => x.RequiresAdminUnlock).HasDefaultValue(false);
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Scope).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions", "identity");
        builder.HasKey(x => new { x.RoleId, x.PermissionId });
    }
}

public sealed class AccountRoleConfiguration : IEntityTypeConfiguration<AccountRole>
{
    public void Configure(EntityTypeBuilder<AccountRole> builder)
    {
        builder.ToTable("account_roles", "identity");
        builder.HasKey(x => new { x.AccountId, x.RoleId, x.MarketCode });
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
    }
}

public sealed class AuthorizationAuditConfiguration : IEntityTypeConfiguration<AuthorizationAudit>
{
    public void Configure(EntityTypeBuilder<AuthorizationAudit> builder)
    {
        builder.ToTable("authorization_audit", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Surface).HasColumnType("citext").IsRequired();
        builder.Property(x => x.PermissionCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Decision).HasColumnType("citext").IsRequired();
        builder.Property(x => x.ReasonCode).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => new { x.Surface, x.PermissionCode, x.Decision, x.OccurredAt });
    }
}

public sealed class RateLimitEventConfiguration : IEntityTypeConfiguration<RateLimitEvent>
{
    public void Configure(EntityTypeBuilder<RateLimitEvent> builder)
    {
        builder.ToTable("rate_limit_events", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PolicyCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Surface).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => new { x.PolicyCode, x.BlockedAt });
    }
}
