using System.Security.Claims;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Customer.RevokeSession;

public static class RevokeSessionHandler
{
    public static async Task<RevokeSessionHandlerResult> HandleAsync(
        ClaimsPrincipal user,
        RevokeSessionRequest request,
        IdentityDbContext dbContext,
        IRefreshTokenRevocationStore revocationStore,
        CancellationToken cancellationToken)
    {
        var subject = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out var accountId))
        {
            return RevokeSessionHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.common.denied",
                "Unauthorized",
                "Authentication is required.");
        }

        var currentSid = user.FindFirstValue("sid");
        if (Guid.TryParse(currentSid, out var currentSessionId) && currentSessionId == request.SessionId)
        {
            return RevokeSessionHandlerResult.Fail(
                StatusCodes.Status403Forbidden,
                "identity.session.revoke_current_forbidden",
                "Cannot revoke current session",
                "Use sign-out to terminate the current session.");
        }

        var now = DateTimeOffset.UtcNow;
        var session = await dbContext.Sessions.SingleOrDefaultAsync(
            x => x.Id == request.SessionId && x.AccountId == accountId && x.Status == "active",
            cancellationToken);

        if (session is null)
        {
            return RevokeSessionHandlerResult.Success();
        }

        session.Status = "revoked";
        session.RevokedAt = now;
        session.RevokedReason = "user_revoke_session";

        var activeRefreshTokens = await dbContext.RefreshTokens
            .Where(x => x.SessionId == session.Id && x.Status == "active")
            .ToListAsync(cancellationToken);

        foreach (var token in activeRefreshTokens)
        {
            token.Status = "revoked";
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await revocationStore.RevokeBySessionAsync(session.Id, "user_revoke_session", accountId, cancellationToken);
        return RevokeSessionHandlerResult.Success();
    }
}

public sealed record RevokeSessionHandlerResult(
    bool IsSuccess,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static RevokeSessionHandlerResult Success() =>
        new(true, StatusCodes.Status204NoContent, null, null, null);

    public static RevokeSessionHandlerResult Fail(int statusCode, string reasonCode, string title, string detail) =>
        new(false, statusCode, reasonCode, title, detail);
}
