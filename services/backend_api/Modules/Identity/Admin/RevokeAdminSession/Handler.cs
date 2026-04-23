using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Admin.RevokeAdminSession;

public static class RevokeAdminSessionHandler
{
    public static async Task HandleAsync(
        RevokeAdminSessionRequest request,
        IdentityDbContext dbContext,
        IRefreshTokenRevocationStore revocationStore,
        Guid actorAccountId,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var session = await dbContext.Sessions.SingleOrDefaultAsync(
            x => x.Id == request.SessionId
                 && x.AccountId == request.AccountId
                 && x.Surface == "admin"
                 && x.Status == "active",
            cancellationToken);

        if (session is null)
        {
            return;
        }

        session.Status = "revoked";
        session.RevokedAt = now;
        session.RevokedReason = "admin_revoke_session";

        var activeRefreshTokens = await dbContext.RefreshTokens
            .Where(x => x.SessionId == session.Id && x.Status == "active")
            .ToListAsync(cancellationToken);

        foreach (var refreshToken in activeRefreshTokens)
        {
            refreshToken.Status = "revoked";
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await revocationStore.RevokeBySessionAsync(session.Id, "admin_revoke_session", request.AccountId, cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: actorAccountId,
                ActorRole: "admin",
                Action: "admin.session.revoked",
                EntityType: nameof(Session),
                EntityId: session.Id,
                BeforeState: new { Status = "active" },
                AfterState: new { session.Status, session.RevokedAt, session.RevokedReason, request.AccountId },
                Reason: "admin_revoke_session"),
            cancellationToken);
    }
}
