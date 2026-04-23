using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Customer.RefreshSession;

public static class RefreshSessionHandler
{
    public static async Task<RefreshSessionHandlerResult> HandleAsync(
        RefreshSessionRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        CustomerAuthSessionService authSessionService,
        IdentityTokenSecretHasher tokenSecretHasher,
        IdentityClientFingerprintHasher fingerprintHasher,
        IRefreshTokenRevocationStore revocationStore,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (!IdentityOpaqueTokenCodec.TryParse(request.RefreshToken, out var refreshToken))
        {
            return RefreshSessionHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.refresh.invalid",
                "Invalid refresh token",
                "The refresh token is invalid.");
        }

        var matched = await dbContext.RefreshTokens.SingleOrDefaultAsync(
            x => x.TokenId == refreshToken.TokenId && (x.Status == "active" || x.Status == "consumed"),
            cancellationToken);

        if (matched is null)
        {
            return RefreshSessionHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.refresh.invalid",
                "Invalid refresh token",
                "The refresh token is invalid.");
        }

        var storedHash = matched.TokenSecretHash ?? matched.TokenHash;
        if (storedHash is null || !tokenSecretHasher.Verify(refreshToken.Secret, storedHash))
        {
            return RefreshSessionHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.refresh.invalid",
                "Invalid refresh token",
                "The refresh token is invalid.");
        }

        var session = await dbContext.Sessions.SingleOrDefaultAsync(x => x.Id == matched.SessionId, cancellationToken);
        if (session is null)
        {
            return RefreshSessionHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.refresh.invalid",
                "Invalid refresh token",
                "The refresh token is invalid.");
        }

        if (!string.Equals(session.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return RefreshSessionHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.refresh.invalid",
                "Invalid refresh token",
                "The refresh token is invalid.");
        }

        if (await revocationStore.IsRevokedAsync(storedHash, cancellationToken))
        {
            await RevokeSessionChainAsync(dbContext, revocationStore, session, "refresh_revoked", now, cancellationToken);
            return RefreshSessionHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.refresh.invalid",
                "Invalid refresh token",
                "The refresh token is invalid.");
        }

        var currentUserAgent = httpContext.Request.Headers.UserAgent.ToString();
        var currentIp = httpContext.Connection.RemoteIpAddress?.ToString();
        if (session.ClientFingerprintHash is null
            || !fingerprintHasher.Verify(session.ClientFingerprintHash, currentUserAgent, currentIp))
        {
            await RevokeSessionChainAsync(
                dbContext,
                revocationStore,
                session,
                "refresh_fingerprint_mismatch",
                now,
                cancellationToken);

            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: session.AccountId,
                    ActorRole: "customer",
                    Action: "identity.refresh.fingerprint_mismatch",
                    EntityType: nameof(Session),
                    EntityId: session.Id,
                    BeforeState: null,
                    AfterState: new
                    {
                        session.Id,
                        session.AccountId,
                        currentUserAgent,
                        currentIp,
                    },
                    Reason: "refresh.replay"),
                cancellationToken);

            return RefreshSessionHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.refresh.invalid",
                "Invalid refresh token",
                "The refresh token is invalid.");
        }

        if (matched.Status == "consumed")
        {
            await RevokeSessionChainAsync(dbContext, revocationStore, session, "refresh_reuse", now, cancellationToken);
            return RefreshSessionHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.refresh.invalid",
                "Invalid refresh token",
                "The refresh token is invalid.");
        }

        if (matched.ExpiresAt <= now)
        {
            matched.Status = "revoked";
            await dbContext.SaveChangesAsync(cancellationToken);
            return RefreshSessionHandlerResult.Fail(
                StatusCodes.Status410Gone,
                "identity.refresh.expired",
                "Refresh token expired",
                "The refresh token has expired.");
        }

        var account = await dbContext.Accounts
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == session.AccountId, cancellationToken);
        if (account is null || !string.Equals(account.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            await RevokeSessionChainAsync(dbContext, revocationStore, session, "account_inactive", now, cancellationToken);
            return RefreshSessionHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.refresh.account_inactive",
                "Account is unavailable",
                "The account is no longer active.");
        }

        matched.Status = "consumed";
        matched.ConsumedAt = now;

        var authSession = authSessionService.CreateForExistingSession(account, session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return RefreshSessionHandlerResult.Success(authSession);
    }

    private static async Task RevokeSessionChainAsync(
        IdentityDbContext dbContext,
        IRefreshTokenRevocationStore revocationStore,
        Session session,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        session.Status = "revoked";
        session.RevokedAt = now;
        session.RevokedReason = reason;

        var active = await dbContext.RefreshTokens
            .Where(x => x.SessionId == session.Id && x.Status == "active")
            .ToListAsync(cancellationToken);

        foreach (var token in active)
        {
            token.Status = "revoked";
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await revocationStore.RevokeBySessionAsync(session.Id, reason, session.AccountId, cancellationToken);
    }
}

public sealed record RefreshSessionHandlerResult(
    bool IsSuccess,
    AuthSessionResponse? Session,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static RefreshSessionHandlerResult Success(AuthSessionResponse session)
    {
        return new RefreshSessionHandlerResult(true, session, StatusCodes.Status200OK, null, null, null);
    }

    public static RefreshSessionHandlerResult Fail(int statusCode, string reasonCode, string title, string detail)
    {
        return new RefreshSessionHandlerResult(false, null, statusCode, reasonCode, title, detail);
    }
}
