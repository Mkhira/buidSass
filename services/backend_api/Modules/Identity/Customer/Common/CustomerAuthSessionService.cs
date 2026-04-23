using System.Security.Claims;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;

namespace BackendApi.Modules.Identity.Customer.Common;

public sealed class CustomerAuthSessionService(
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

    public AuthSessionResponse CreateForNewSession(Account account, HttpContext httpContext)
    {
        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid();
        var userAgent = ResolveUserAgent(httpContext);
        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();

        _dbContext.Sessions.Add(new Session
        {
            Id = sessionId,
            AccountId = account.Id,
            Surface = "customer",
            CreatedAt = now,
            LastSeenAt = now,
            ClientAgent = userAgent,
            ClientIpHash = _clientSecurityHasher.HashIp(remoteIp),
            ClientFingerprintHash = _clientFingerprintHasher.Hash(userAgent, remoteIp),
            Status = "active",
        });

        return CreateResponse(account, sessionId, now);
    }

    public AuthSessionResponse CreateForExistingSession(Account account, Session session)
    {
        var now = DateTimeOffset.UtcNow;
        session.LastSeenAt = now;
        return CreateResponse(account, session.Id, now);
    }

    private AuthSessionResponse CreateResponse(Account account, Guid sessionId, DateTimeOffset now)
    {
        var claims = new List<Claim>
        {
            new("market_code", account.MarketCode),
            new("sid", sessionId.ToString()),
            new("permission_version", account.PermissionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };

        var jwt = _jwtIssuer.IssueAccessToken(
            new JwtIssueRequest(
                SurfaceKind.Customer,
                account.Id.ToString(),
                claims));

        var refreshToken = IdentityOpaqueTokenCodec.Create();
        var refreshHash = _tokenSecretHasher.HashSecret(refreshToken.Secret);

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TokenId = refreshToken.TokenId,
            TokenSecretHash = refreshHash,
            TokenHash = refreshHash,
            IssuedAt = now,
            ExpiresAt = now.AddDays(30),
            Status = "active",
        });

        return new AuthSessionResponse(
            jwt.AccessToken,
            jwt.ExpiresAt,
            refreshToken.ToString(),
            now.AddDays(30));
    }

    private static string ResolveUserAgent(HttpContext httpContext)
    {
        return httpContext.Request.Headers.UserAgent.ToString();
    }
}

public sealed record AuthSessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
