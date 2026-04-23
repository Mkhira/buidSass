using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Admin.ResetAdminMfa;

public static class ResetAdminMfaHandler
{
    public static async Task<int> HandleAsync(
        Guid accountId,
        Guid actorAccountId,
        IdentityDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var factors = await dbContext.AdminMfaFactors
            .Where(x => x.AccountId == accountId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var factor in factors)
        {
            factor.RevokedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: actorAccountId,
                ActorRole: "admin",
                Action: "admin.mfa.reset_by_super_admin",
                EntityType: "admin_mfa_factor",
                EntityId: accountId,
                BeforeState: new { ActiveFactorCount = factors.Count },
                AfterState: new { RevokedFactorCount = factors.Count },
                Reason: "mfa_reset"),
            cancellationToken);

        return factors.Count;
    }
}
