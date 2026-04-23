using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Admin.RevokeInvitation;

public static class RevokeInvitationHandler
{
    public static async Task HandleAsync(
        RevokeInvitationRequest request,
        Guid actorAccountId,
        IdentityDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var invitation = await dbContext.AdminInvitations
            .SingleOrDefaultAsync(x => x.Id == request.InvitationId && x.Status == "pending", cancellationToken);

        if (invitation is null)
        {
            return;
        }

        var beforeStatus = invitation.Status;
        invitation.Status = "revoked";
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: actorAccountId,
                ActorRole: "admin",
                Action: "admin.invitation.revoked",
                EntityType: nameof(AdminInvitation),
                EntityId: invitation.Id,
                BeforeState: new { Status = beforeStatus },
                AfterState: new { invitation.Status, invitation.EmailNormalized, invitation.InvitedRoleId },
                Reason: "admin.invitation.revoke"),
            cancellationToken);
    }
}
