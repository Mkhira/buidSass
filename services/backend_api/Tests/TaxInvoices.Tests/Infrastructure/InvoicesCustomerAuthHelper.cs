using System.Net.Http.Headers;
using System.Security.Claims;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace TaxInvoices.Tests.Infrastructure;

public static class InvoicesCustomerAuthHelper
{
    public static async Task<(string AccessToken, Guid AccountId)> IssueCustomerTokenAsync(
        InvoicesTestFactory factory,
        string marketCode = "ksa")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var jwtIssuer = scope.ServiceProvider.GetRequiredService<IJwtIssuer>();
        var now = DateTimeOffset.UtcNow;
        var accountId = Guid.NewGuid();
        var email = $"inv-cust-{accountId:N}@example.test";
        db.Accounts.Add(new Account
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
            DisplayName = "Invoice Customer",
            ProfessionalVerificationStatus = "unverified",
            CreatedAt = now,
            UpdatedAt = now,
        });
        var sessionId = Guid.NewGuid();
        db.Sessions.Add(new Session
        {
            Id = sessionId, AccountId = accountId, Surface = "customer",
            CreatedAt = now, LastSeenAt = now,
            ClientIpHash = Array.Empty<byte>(), Status = "active",
        });
        await db.SaveChangesAsync();
        var jwt = jwtIssuer.IssueAccessToken(new JwtIssueRequest(SurfaceKind.Customer, accountId.ToString(),
            new List<Claim>
            {
                new("market_code", marketCode),
                new("sid", sessionId.ToString()),
                new("permission_version", "1"),
            }));
        return (jwt.AccessToken, accountId);
    }

    public static void SetBearer(HttpClient client, string accessToken)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
}
