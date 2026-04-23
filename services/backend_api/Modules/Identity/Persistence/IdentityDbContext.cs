using BackendApi.Modules.Identity.Entities;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RevokedRefreshToken> RevokedRefreshTokens => Set<RevokedRefreshToken>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<EmailVerificationChallenge> EmailVerificationChallenges => Set<EmailVerificationChallenge>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<AdminInvitation> AdminInvitations => Set<AdminInvitation>();
    public DbSet<AdminMfaFactor> AdminMfaFactors => Set<AdminMfaFactor>();
    public DbSet<AdminMfaReplayGuard> AdminMfaReplayGuards => Set<AdminMfaReplayGuard>();
    public DbSet<AdminPartialAuthToken> AdminPartialAuthTokens => Set<AdminPartialAuthToken>();
    public DbSet<AdminMfaChallenge> AdminMfaChallenges => Set<AdminMfaChallenge>();
    public DbSet<LockoutState> LockoutStates => Set<LockoutState>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AccountRole> AccountRoles => Set<AccountRole>();
    public DbSet<AuthorizationAudit> AuthorizationAudits => Set<AuthorizationAudit>();
    public DbSet<RateLimitEvent> RateLimitEvents => Set<RateLimitEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        // Only apply configurations from the Identity namespace so sibling modules (e.g. Catalog)
        // in the same assembly don't bleed their entities into this context's model.
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(IdentityDbContext).Assembly,
            type => type.Namespace?.StartsWith("BackendApi.Modules.Identity", StringComparison.Ordinal) == true);
    }
}
