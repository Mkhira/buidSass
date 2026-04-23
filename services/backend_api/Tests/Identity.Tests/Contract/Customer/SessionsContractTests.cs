using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BackendApi.Modules.Identity.Persistence;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Contract.Customer;

public sealed class SessionsContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task ListSessions_AuthenticatedCustomer_ReturnsOwn()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "sessions-list@local.dev",
            password: "StrongPassword!123");

        var firstSignIn = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));
        var firstSession = await firstSignIn.Content.ReadFromJsonAsync<AuthSessionResponse>();

        var secondSignIn = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));
        var secondSession = await secondSignIn.Content.ReadFromJsonAsync<AuthSessionResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstSession!.AccessToken);
        var response = await client.GetAsync("/v1/customer/identity/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CustomerSessionsResponse>();

        body.Should().NotBeNull();
        body!.Sessions.Should().HaveCount(2);
        var currentId = ParseSessionId(firstSession.AccessToken);
        body.Sessions.Single(x => x.Id == currentId).IsCurrent.Should().BeTrue();
        body.Sessions.Should().Contain(x => x.Id == ParseSessionId(secondSession!.AccessToken));
    }

    [Fact]
    public async Task RevokeSession_NonCurrent_ReturnsNoContent()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "sessions-revoke@local.dev",
            password: "StrongPassword!123");

        var firstSignIn = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));
        var firstSession = await firstSignIn.Content.ReadFromJsonAsync<AuthSessionResponse>();

        var secondSignIn = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));
        var secondSession = await secondSignIn.Content.ReadFromJsonAsync<AuthSessionResponse>();

        var secondSessionId = ParseSessionId(secondSession!.AccessToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstSession!.AccessToken);

        var revoke = await client.DeleteAsync($"/v1/customer/identity/sessions/{secondSessionId}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var revoked = await db.Sessions.SingleAsync(x => x.Id == secondSessionId);
        revoked.Status.Should().Be("revoked");
    }

    [Fact]
    public async Task RevokeSession_Current_Returns403()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "sessions-current@local.dev",
            password: "StrongPassword!123");

        var signIn = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));
        var session = await signIn.Content.ReadFromJsonAsync<AuthSessionResponse>();
        var currentSessionId = ParseSessionId(session!.AccessToken);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var revoke = await client.DeleteAsync($"/v1/customer/identity/sessions/{currentSessionId}");

        revoke.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static Guid ParseSessionId(string jwt)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        var sid = token.Claims.Single(x => x.Type == "sid").Value;
        return Guid.Parse(sid);
    }
}

public sealed record CustomerSessionsResponse(IReadOnlyCollection<CustomerSessionItem> Sessions);

public sealed record CustomerSessionItem(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    string? ClientAgent,
    bool IsCurrent);
