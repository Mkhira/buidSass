using System.Security.Claims;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Customer.ChangePassword;

public static class ChangePasswordHandler
{
    public static async Task<ChangePasswordHandlerResult> HandleAsync(
        ClaimsPrincipal user,
        ChangePasswordRequest request,
        IdentityDbContext dbContext,
        Argon2idHasher hasher,
        BreachListChecker breachListChecker,
        IRefreshTokenRevocationStore revocationStore,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var subject = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out var accountId))
        {
            return ChangePasswordHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.common.denied",
                "Unauthorized",
                "Authentication is required.");
        }

        var account = await dbContext.Accounts.SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        if (account is null)
        {
            return ChangePasswordHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.common.denied",
                "Unauthorized",
                "Authentication is required.");
        }

        var verify = hasher.VerifyAndRehashIfNeeded(request.CurrentPassword, account.PasswordHash, SurfaceKind.Customer);
        if (!verify.IsValid)
        {
            return ChangePasswordHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.password_change.invalid_current",
                "Invalid current password",
                "The current password is incorrect.");
        }

        if (breachListChecker.IsCompromised(request.NewPassword))
        {
            return ChangePasswordHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.password_change.password_too_weak",
                "Weak password",
                "The password appears in a known breached-password list.");
        }

        var now = DateTimeOffset.UtcNow;
        account.PasswordHash = hasher.HashPassword(request.NewPassword, SurfaceKind.Customer);
        account.UpdatedAt = now;

        Guid.TryParse(user.FindFirstValue("sid"), out var currentSessionId);

        var sessionsToRevoke = await dbContext.Sessions
            .Where(x => x.AccountId == accountId
                        && x.Status == "active"
                        && x.Id != currentSessionId)
            .ToListAsync(cancellationToken);

        foreach (var session in sessionsToRevoke)
        {
            session.Status = "revoked";
            session.RevokedAt = now;
            session.RevokedReason = "password_change";
        }

        var sessionIds = sessionsToRevoke.Select(x => x.Id).ToHashSet();
        var refreshTokens = await dbContext.RefreshTokens
            .Where(x => sessionIds.Contains(x.SessionId) && x.Status == "active")
            .ToListAsync(cancellationToken);

        foreach (var refreshToken in refreshTokens)
        {
            refreshToken.Status = "revoked";
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var session in sessionsToRevoke)
        {
            await revocationStore.RevokeBySessionAsync(
                session.Id,
                "password_change",
                accountId,
                cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: accountId,
                ActorRole: "customer",
                Action: "password.changed",
                EntityType: nameof(Account),
                EntityId: accountId,
                BeforeState: null,
                AfterState: new
                {
                    AccountId = accountId,
                    RevokedSessionCount = sessionsToRevoke.Count,
                    account.UpdatedAt,
                },
                Reason: "customer.password_change"),
            cancellationToken);

        return ChangePasswordHandlerResult.Success();
    }
}

public sealed record ChangePasswordHandlerResult(
    bool IsSuccess,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static ChangePasswordHandlerResult Success() =>
        new(true, StatusCodes.Status200OK, null, null, null);

    public static ChangePasswordHandlerResult Fail(int statusCode, string reasonCode, string title, string detail) =>
        new(false, statusCode, reasonCode, title, detail);
}
