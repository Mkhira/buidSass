using System.Security.Claims;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Customer.SignOut;

public static class SignOutHandler
{
    public static async Task HandleAsync(
        ClaimsPrincipal user,
        SignOutRequest request,
        IdentityDbContext dbContext,
        IdentityTokenSecretHasher tokenSecretHasher,
        IRefreshTokenRevocationStore revocationStore,
        CancellationToken cancellationToken)
    {
        var accountId = ResolveGuid(user.FindFirstValue("sub"))
            ?? ResolveGuid(user.FindFirstValue(ClaimTypes.NameIdentifier));
        var sessionId = ResolveGuid(user.FindFirstValue("sid"));

        if (accountId is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var sessions = await dbContext.Sessions
            .Where(x => x.AccountId == accountId.Value && x.Status == "active")
            .ToListAsync(cancellationToken);

        if (sessionId is Guid sid)
        {
            sessions = sessions.Where(x => x.Id == sid).ToList();
        }

        foreach (var session in sessions)
        {
            session.Status = "revoked";
            session.RevokedAt = now;
            session.RevokedReason = "user_signout";
        }

        var sessionIds = sessions.Select(x => x.Id).ToHashSet();
        var activeRefreshTokens = await dbContext.RefreshTokens
            .Where(x => sessionIds.Contains(x.SessionId) && x.Status == "active")
            .ToListAsync(cancellationToken);

        foreach (var token in activeRefreshTokens)
        {
            token.Status = "revoked";
        }

        if (!string.IsNullOrWhiteSpace(request.RefreshToken)
            && IdentityOpaqueTokenCodec.TryParse(request.RefreshToken, out var parsedToken))
        {
            var candidate = await (
                from token in dbContext.RefreshTokens
                join session in dbContext.Sessions on token.SessionId equals session.Id
                where token.TokenId == parsedToken.TokenId
                      && token.Status == "active"
                      && session.AccountId == accountId.Value
                select token)
                .SingleOrDefaultAsync(
                cancellationToken);

            if (candidate is not null)
            {
                var storedHash = candidate.TokenSecretHash ?? candidate.TokenHash;
                if (storedHash is not null && tokenSecretHasher.Verify(parsedToken.Secret, storedHash))
                {
                    candidate.Status = "revoked";
                    await dbContext.SaveChangesAsync(cancellationToken);

                    var revocationHash = RefreshTokenHashResolver.Resolve(candidate);
                    if (revocationHash.Length > 0)
                    {
                        await revocationStore.RevokeAsync(
                            revocationHash,
                            "user_signout",
                            accountId,
                            cancellationToken);
                    }
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var session in sessions)
        {
            await revocationStore.RevokeBySessionAsync(
                session.Id,
                "user_signout",
                accountId,
                cancellationToken);
        }
    }

    private static Guid? ResolveGuid(string? raw)
    {
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }
}
