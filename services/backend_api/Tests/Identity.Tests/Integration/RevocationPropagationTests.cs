using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Integration;

public sealed class RevocationPropagationTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task RevocationPropagates_UnderSixtySeconds()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "revocation@local.dev",
            password: "StrongPassword!123");

        var firstSignIn = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));
        firstSignIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstSession = await firstSignIn.Content.ReadFromJsonAsync<AuthSessionResponse>();

        var secondSignIn = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));
        secondSignIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondSession = await secondSignIn.Content.ReadFromJsonAsync<AuthSessionResponse>();

        var secondSessionId = ParseSessionId(secondSession!.AccessToken);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstSession!.AccessToken);
        var revoke = await client.DeleteAsync($"/v1/customer/identity/sessions/{secondSessionId}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var refreshClient = factory.CreateClient();
        refreshClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", secondSession.AccessToken);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        HttpStatusCode? lastStatus = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var refresh = await refreshClient.PostAsJsonAsync(
                "/v1/customer/identity/session/refresh",
                new RefreshSessionRequest(secondSession.RefreshToken));

            lastStatus = refresh.StatusCode;
            if (refresh.StatusCode == HttpStatusCode.Unauthorized)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        lastStatus.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshSession_InactiveAccount_RevokesSessionAndReturnsUnauthorized()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "refresh-inactive@local.dev",
            password: "StrongPassword!123");

        var signIn = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await signIn.Content.ReadFromJsonAsync<AuthSessionResponse>();
        var sessionId = ParseSessionId(session!.AccessToken);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var account = await db.Accounts.SingleAsync(x => x.Id == seed.AccountId);
            account.Status = "disabled";
            await db.SaveChangesAsync();
        }

        using var refreshClient = factory.CreateClient();
        refreshClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var refresh = await refreshClient.PostAsJsonAsync(
            "/v1/customer/identity/session/refresh",
            new RefreshSessionRequest(session.RefreshToken));

        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await refresh.Content.ReadFromJsonAsync<RefreshProblemResponse>();
        body!.ReasonCode.Should().Be("identity.refresh.account_inactive");

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var revokedSession = await verifyDb.Sessions.SingleAsync(x => x.Id == sessionId);
        revokedSession.Status.Should().Be("revoked");
    }

    [Fact]
    public async Task RefreshSession_FingerprintMismatch_RevokesSessionAndEmitsSecurityAudit()
    {
        await factory.ResetDatabaseAsync();
        var signInClient = factory.CreateClient();
        signInClient.DefaultRequestHeaders.UserAgent.Clear();
        signInClient.DefaultRequestHeaders.UserAgent.ParseAdd("IdentityTests/SignInClient");

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "refresh-fingerprint@local.dev",
            password: "StrongPassword!123");

        var signIn = await signInClient.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await signIn.Content.ReadFromJsonAsync<AuthSessionResponse>();
        var sessionId = ParseSessionId(session!.AccessToken);

        using var refreshClient = factory.CreateClient();
        refreshClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);
        refreshClient.DefaultRequestHeaders.UserAgent.Clear();
        refreshClient.DefaultRequestHeaders.UserAgent.ParseAdd("IdentityTests/ReplayClient");

        var refresh = await refreshClient.PostAsJsonAsync(
            "/v1/customer/identity/session/refresh",
            new RefreshSessionRequest(session.RefreshToken));

        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await refresh.Content.ReadFromJsonAsync<RefreshProblemResponse>();
        body!.ReasonCode.Should().Be("identity.refresh.invalid");

        await using var scope = factory.Services.CreateAsyncScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var revokedSession = await identityDb.Sessions.SingleAsync(x => x.Id == sessionId);
        revokedSession.Status.Should().Be("revoked");
        revokedSession.RevokedReason.Should().Be("refresh_fingerprint_mismatch");

        var hasAudit = await appDb.AuditLogEntries
            .AnyAsync(x =>
                x.Action == "identity.refresh.fingerprint_mismatch"
                && x.EntityType == "Session"
                && x.EntityId == sessionId);
        hasAudit.Should().BeTrue();
    }

    private static Guid ParseSessionId(string jwt)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        var sid = token.Claims.Single(x => x.Type == "sid").Value;
        return Guid.Parse(sid);
    }
}

public sealed record RefreshSessionRequest(string RefreshToken);
public sealed record RefreshProblemResponse(string? ReasonCode);
public sealed record CustomerSignInRequest(string Identifier, string Password);
