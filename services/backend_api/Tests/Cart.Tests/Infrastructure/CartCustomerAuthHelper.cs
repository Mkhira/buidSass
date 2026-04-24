using System.Net.Http.Headers;
using System.Security.Claims;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Infrastructure;

public static class CartCustomerAuthHelper
{
    public static async Task<(string AccessToken, Guid AccountId)> IssueCustomerTokenAsync(
        CartTestFactory factory,
        string marketCode,
        string? professionalVerificationStatus = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var jwtIssuer = scope.ServiceProvider.GetRequiredService<IJwtIssuer>();

        var now = DateTimeOffset.UtcNow;
        var accountId = Guid.NewGuid();
        var email = $"cart-customer-{accountId:N}@example.test";

        var account = new Account
        {
            Id = accountId,
            Surface = "customer",
            MarketCode = marketCode,
            EmailNormalized = email.ToLowerInvariant(),
            EmailDisplay = email,
            PasswordHash = "x",
            PasswordHashVersion = 1,
            PermissionVersion = 1,
            Status = "active",
            EmailVerifiedAt = now,
            Locale = "en",
            DisplayName = "Cart Customer",
            ProfessionalVerificationStatus = professionalVerificationStatus ?? "unverified",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Accounts.Add(account);

        var sessionId = Guid.NewGuid();
        db.Sessions.Add(new Session
        {
            Id = sessionId,
            AccountId = account.Id,
            Surface = "customer",
            CreatedAt = now,
            LastSeenAt = now,
            ClientIpHash = Array.Empty<byte>(),
            Status = "active",
        });
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new("market_code", marketCode),
            new("sid", sessionId.ToString()),
            new("permission_version", "1"),
        };

        var jwt = jwtIssuer.IssueAccessToken(new JwtIssueRequest(
            SurfaceKind.Customer, account.Id.ToString(), claims));

        return (jwt.AccessToken, account.Id);
    }

    public static void SetBearer(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }
}
