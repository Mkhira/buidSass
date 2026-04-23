using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Identity.Tests.Infrastructure;

namespace Identity.Tests.Integration;

public sealed class JwtCrossInstanceValidationTests
{
    [Fact]
    public async Task CustomerToken_IssuedByOneInstance_IsAcceptedByAnotherInstance_WhenPemMatches()
    {
        await using var issuerFactory = new IdentityTestFactory();
        await issuerFactory.InitializeAsync();
        await issuerFactory.ResetDatabaseAsync();

        await using var validatorFactory = new IdentityTestFactory();
        await validatorFactory.InitializeAsync();
        await validatorFactory.ResetDatabaseAsync();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            issuerFactory,
            email: "cross-instance@local.dev",
            password: "StrongPassword!123");

        var issuerClient = issuerFactory.CreateClient();
        var signIn = await issuerClient.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CrossInstanceCustomerSignInRequest(seed.Email, seed.Password));

        signIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await signIn.Content.ReadFromJsonAsync<CrossInstanceAuthSessionResponse>();
        session.Should().NotBeNull();

        var validatorClient = validatorFactory.CreateClient();
        validatorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var protectedResponse = await validatorClient.GetAsync("/v1/customer/identity/_test/protected");
        protectedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

public sealed record CrossInstanceCustomerSignInRequest(string Identifier, string Password);
public sealed record CrossInstanceAuthSessionResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
