using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Customer.CompletePasswordReset;

public static class CompletePasswordResetHandler
{
    public static async Task<CompletePasswordResetHandlerResult> HandleAsync(
        CompletePasswordResetRequest request,
        IdentityDbContext dbContext,
        Argon2idHasher hasher,
        BreachListChecker breachListChecker,
        IdentityTokenSecretHasher tokenSecretHasher,
        IRefreshTokenRevocationStore revocationStore,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (request.NewPassword.Length < 10)
        {
            return CompletePasswordResetHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.password_reset.password_too_weak",
                "Weak password",
                "The new password does not meet minimum length requirements.");
        }

        if (!IdentityOpaqueTokenCodec.TryParse(request.Token, out var tokenComponents))
        {
            return CompletePasswordResetHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.password_reset.invalid",
                "Invalid password reset token",
                "The password reset token is invalid.");
        }

        var token = await dbContext.PasswordResetTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Status == "pending" && x.TokenId == tokenComponents.TokenId,
                cancellationToken);

        if (token is null)
        {
            return CompletePasswordResetHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.password_reset.invalid",
                "Invalid password reset token",
                "The password reset token is invalid.");
        }

        var storedHash = token.TokenSecretHash ?? token.TokenHash;
        if (storedHash is null || !tokenSecretHasher.Verify(tokenComponents.Secret, storedHash))
        {
            return CompletePasswordResetHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.password_reset.invalid",
                "Invalid password reset token",
                "The password reset token is invalid.");
        }

        if (token.ExpiresAt <= now)
        {
            token.Status = "expired";
            token.CompletedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            return CompletePasswordResetHandlerResult.Fail(
                StatusCodes.Status410Gone,
                "identity.password_reset.expired",
                "Password reset token expired",
                "The password reset token has expired.");
        }

        if (breachListChecker.IsCompromised(request.NewPassword))
        {
            return CompletePasswordResetHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.password_reset.password_too_weak",
                "Weak password",
                "The password appears in a known breached-password list.");
        }

        var account = await dbContext.Accounts.SingleOrDefaultAsync(x => x.Id == token.AccountId, cancellationToken);
        if (account is null)
        {
            return CompletePasswordResetHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.password_reset.invalid",
                "Invalid password reset token",
                "The password reset token is invalid.");
        }

        account.PasswordHash = hasher.HashPassword(request.NewPassword, SurfaceKind.Customer);
        account.UpdatedAt = now;

        var sessions = await dbContext.Sessions
            .Where(x => x.AccountId == account.Id && x.Status == "active")
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Status = "revoked";
            session.RevokedAt = now;
            session.RevokedReason = "password_reset";
        }

        var sessionIds = sessions.Select(x => x.Id).ToHashSet();
        var refreshTokens = await dbContext.RefreshTokens
            .Where(x => sessionIds.Contains(x.SessionId) && x.Status == "active")
            .ToListAsync(cancellationToken);

        foreach (var refreshToken in refreshTokens)
        {
            refreshToken.Status = "revoked";
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var consumedCount = await dbContext.PasswordResetTokens
            .Where(x => x.Id == token.Id && x.Status == "pending")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, "completed")
                    .SetProperty(x => x.CompletedAt, now),
                cancellationToken);

        if (consumedCount == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CompletePasswordResetHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.password_reset.invalid",
                "Invalid password reset token",
                "The password reset token has already been used.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var session in sessions)
        {
            await revocationStore.RevokeBySessionAsync(
                session.Id,
                "password_reset",
                account.Id,
                cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: account.Id,
                ActorRole: "customer",
                Action: "password.reset.completed",
                EntityType: nameof(Account),
                EntityId: account.Id,
                BeforeState: null,
                AfterState: new
                {
                    AccountId = account.Id,
                    TokenId = token.Id,
                    RevokedSessions = sessions.Count,
                },
                Reason: "customer.password_reset"),
            cancellationToken);

        return CompletePasswordResetHandlerResult.Success();
    }
}

public sealed record CompletePasswordResetHandlerResult(
    bool IsSuccess,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static CompletePasswordResetHandlerResult Success() =>
        new(true, StatusCodes.Status200OK, null, null, null);

    public static CompletePasswordResetHandlerResult Fail(int statusCode, string reasonCode, string title, string detail) =>
        new(false, statusCode, reasonCode, title, detail);
}
