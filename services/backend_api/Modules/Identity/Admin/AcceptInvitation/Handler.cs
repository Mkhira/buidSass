using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using BackendApi.Modules.Identity.Primitives.StateMachines;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace BackendApi.Modules.Identity.Admin.AcceptInvitation;

public static class AcceptInvitationHandler
{
    public static async Task<AcceptInvitationHandlerResult> HandleAsync(
        AcceptInvitationRequest request,
        IdentityDbContext dbContext,
        Argon2idHasher hasher,
        BreachListChecker breachListChecker,
        AdminPartialAuthTokenStore partialAuthStore,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        if (breachListChecker.IsCompromised(request.NewPassword))
        {
            return AcceptInvitationHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.invitation.password_too_weak",
                "Weak password",
                "The password appears in a known breached-password list.");
        }

        var tokenHash = AdminIdentityResponseFactory.HashString(request.Token);

        var invitation = await dbContext.AdminInvitations.SingleOrDefaultAsync(
            x => x.Status == "pending" && x.TokenHash == tokenHash,
            cancellationToken);

        if (invitation is null)
        {
            return AcceptInvitationHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.invitation.invalid",
                "Invalid invitation",
                "The invitation token is invalid.");
        }

        if (invitation.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            var stateMachine = new AdminInvitationStateMachine();
            _ = stateMachine.TryTransition(AdminInvitationState.Pending, AdminInvitationTrigger.Expires, out _);
            invitation.Status = "expired";
            await dbContext.SaveChangesAsync(cancellationToken);

            return AcceptInvitationHandlerResult.Fail(
                StatusCodes.Status410Gone,
                "identity.invitation.expired",
                "Invitation expired",
                "The invitation token has expired.");
        }

        var normalizedEmail = invitation.EmailNormalized.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        var account = await dbContext.Accounts.SingleOrDefaultAsync(
            x => x.Surface == "admin" && x.EmailNormalized == normalizedEmail,
            cancellationToken);

        if (account is null)
        {
            account = new Account
            {
                Id = Guid.NewGuid(),
                Surface = "admin",
                MarketCode = "platform",
                EmailNormalized = normalizedEmail,
                EmailDisplay = invitation.EmailNormalized,
                CreatedAt = now,
            };
            dbContext.Accounts.Add(account);
        }

        account.PasswordHash = hasher.HashPassword(request.NewPassword, SurfaceKind.Admin);
        account.PasswordHashVersion = 1;
        account.Status = "active";
        account.EmailVerifiedAt = now;
        account.Locale = "en";
        account.UpdatedAt = now;

        var accountRoleExists = await dbContext.AccountRoles.AnyAsync(
            x => x.AccountId == account.Id
                && x.RoleId == invitation.InvitedRoleId
                && x.MarketCode == "platform",
            cancellationToken);

        if (!accountRoleExists)
        {
            dbContext.AccountRoles.Add(new AccountRole
            {
                AccountId = account.Id,
                RoleId = invitation.InvitedRoleId,
                MarketCode = "platform",
                GrantedByAccountId = invitation.InvitedByAccountId,
                GrantedAt = now,
            });
        }

        var invitationStateMachine = new AdminInvitationStateMachine();
        _ = invitationStateMachine.TryTransition(AdminInvitationState.Pending, AdminInvitationTrigger.Accept, out _);
        invitation.Status = "accepted";
        invitation.AcceptedAccountId = account.Id;
        invitation.AcceptedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: account.Id,
                ActorRole: "admin",
                Action: "admin.invitation.accepted",
                EntityType: nameof(AdminInvitation),
                EntityId: invitation.Id,
                BeforeState: new { invitation.Status, InvitedBy = invitation.InvitedByAccountId },
                AfterState: new { account.Id, account.EmailDisplay, invitation.InvitedRoleId },
                Reason: "admin.invitation.accept"),
            cancellationToken);

        var partialAuthToken = await partialAuthStore.IssueAsync(account.Id, TimeSpan.FromMinutes(30), cancellationToken);
        return AcceptInvitationHandlerResult.Success(partialAuthToken);
    }
}

public sealed record AcceptInvitationHandlerResult(
    bool IsSuccess,
    string? PartialAuthToken,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static AcceptInvitationHandlerResult Success(string token)
    {
        return new AcceptInvitationHandlerResult(true, token, StatusCodes.Status202Accepted, null, null, null);
    }

    public static AcceptInvitationHandlerResult Fail(
        int statusCode,
        string reasonCode,
        string title,
        string detail)
    {
        return new AcceptInvitationHandlerResult(false, null, statusCode, reasonCode, title, detail);
    }
}
