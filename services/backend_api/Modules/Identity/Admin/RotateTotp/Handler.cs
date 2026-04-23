using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Admin.RotateTotp;

public static class RotateTotpHandler
{
    public static async Task<bool> HandleAsync(
        Guid factorId,
        Guid? actorAccountId,
        IdentityDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var factor = await dbContext.AdminMfaFactors.SingleOrDefaultAsync(x => x.Id == factorId && x.RevokedAt == null, cancellationToken);
        if (factor is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        factor.RevokedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: actorAccountId ?? factor.AccountId,
                ActorRole: "admin",
                Action: "admin.mfa.totp_rotated",
                EntityType: nameof(AdminMfaFactor),
                EntityId: factor.Id,
                BeforeState: new { factor.AccountId, factor.Kind, ConfirmedAt = factor.ConfirmedAt },
                AfterState: new { factor.AccountId, factor.Kind, factor.RevokedAt },
                Reason: "admin.mfa.totp_rotate"),
            cancellationToken);

        return true;
    }
}
