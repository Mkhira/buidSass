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

public sealed class PasswordChangeContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task PasswordChange_AuthenticatedCustomer_RevokesOtherSessions()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "password-change@local.dev",
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

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstSession!.AccessToken);

        var change = await client.PostAsJsonAsync(
            "/v1/customer/identity/password/change",
            new PasswordChangeRequest(seed.Password, "ChangedPassword!456"));

        change.StatusCode.Should().Be(HttpStatusCode.OK);

        var sessionId = ParseSessionId(firstSession.AccessToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var activeSessions = await db.Sessions
            .Where(x => x.AccountId == seed.AccountId && x.Status == "active")
            .ToListAsync();

        activeSessions.Should().ContainSingle();
        activeSessions.Single().Id.Should().Be(sessionId);
    }

    private static Guid ParseSessionId(string jwt)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        var sid = token.Claims.Single(x => x.Type == "sid").Value;
        return Guid.Parse(sid);
    }
}

public sealed record PasswordChangeRequest(string CurrentPassword, string NewPassword);
