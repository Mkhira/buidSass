using System.Security.Claims;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;

namespace BackendApi.Modules.Identity.Admin.Common;

public sealed class AdminAuthSessionService(
    IdentityDbContext dbContext,
    IJwtIssuer jwtIssuer,
    IdentityTokenSecretHasher tokenSecretHasher,
    IdentityClientSecurityHasher clientSecurityHasher,
    IdentityClientFingerprintHasher clientFingerprintHasher)
{
    private readonly IdentityDbContext _dbContext = dbContext;
    private readonly IJwtIssuer _jwtIssuer = jwtIssuer;
    private readonly IdentityTokenSecretHasher _tokenSecretHasher = tokenSecretHasher;
    private readonly IdentityClientSecurityHasher _clientSecurityHasher = clientSecurityHasher;
    private readonly IdentityClientFingerprintHasher _clientFingerprintHasher = clientFingerprintHasher;

    public async Task<AdminAuthSessionResponse> IssueAdminSessionAsync(
        Account account,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid();
        var userAgent = ResolveUserAgent(httpContext);
        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var claims = new List<Claim>
        {
            new("market_code", "platform"),
            new("sid", sessionId.ToString()),
            new("permission_version", account.PermissionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };

        var jwt = _jwtIssuer.IssueAccessToken(
            new JwtIssueRequest(
                SurfaceKind.Admin,
                account.Id.ToString(),
                claims));

        var refreshToken = IdentityOpaqueTokenCodec.Create();
        var refreshTokenHash = _tokenSecretHasher.HashSecret(refreshToken.Secret);

        _dbContext.Sessions.Add(new Session
        {
            Id = sessionId,
            AccountId = account.Id,
            Surface = "admin",
            CreatedAt = now,
            LastSeenAt = now,
            ClientAgent = userAgent,
            ClientIpHash = _clientSecurityHasher.HashIp(remoteIp),
            ClientFingerprintHash = _clientFingerprintHasher.Hash(userAgent, remoteIp),
            Status = "active",
        });

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TokenId = refreshToken.TokenId,
            TokenSecretHash = refreshTokenHash,
            TokenHash = refreshTokenHash,
            IssuedAt = now,
            ExpiresAt = now.AddHours(8),
            Status = "active",
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AdminAuthSessionResponse(
            jwt.AccessToken,
            jwt.ExpiresAt,
            refreshToken.ToString(),
            now.AddHours(8),
            new AdminSessionSummary(sessionId, "admin", account.MarketCode),
            new AdminAccountSummary(account.Id, account.EmailDisplay, account.Locale));
    }

    private static string ResolveUserAgent(HttpContext httpContext)
    {
        return httpContext.Request.Headers.UserAgent.ToString();
    }
}

public sealed record AdminAuthSessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AdminSessionSummary Session,
    AdminAccountSummary Account);

public sealed record AdminSessionSummary(Guid Id, string Surface, string MarketCode);
public sealed record AdminAccountSummary(Guid Id, string EmailDisplay, string Locale);
