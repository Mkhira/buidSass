using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Identity.Tests.Infrastructure;

namespace Identity.Tests.Integration;

public sealed class JwksEndpointTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task CustomerAndAdminJwksEndpoints_ReturnSurfaceSpecificKey()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var customer = await client.GetFromJsonAsync<JwksDocument>("/v1/customer/identity/.well-known/jwks.json");
        var admin = await client.GetFromJsonAsync<JwksDocument>("/v1/admin/identity/.well-known/jwks.json");

        customer.Should().NotBeNull();
        admin.Should().NotBeNull();
        customer!.Keys.Should().ContainSingle();
        admin!.Keys.Should().ContainSingle();
        customer.Keys[0].Kid.Should().Be("test-customer-current");
        admin.Keys[0].Kid.Should().Be("test-admin-current");
    }

    [Fact]
    public async Task CustomerToken_IsRejectedByAdminScheme()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "cross-surface-jwt@local.dev",
            password: "StrongPassword!123");

        var signIn = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CrossSurfaceCustomerSignInRequest(seed.Email, seed.Password));
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await signIn.Content.ReadFromJsonAsync<CrossSurfaceAuthSessionResponse>();
        session.Should().NotBeNull();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);
        var adminProtected = await client.GetAsync("/v1/admin/identity/_test/protected");
        adminProtected.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

public sealed record JwksDocument(IReadOnlyList<JwksKey> Keys);
public sealed record JwksKey(string Kid);
public sealed record CrossSurfaceCustomerSignInRequest(string Identifier, string Password);
public sealed record CrossSurfaceAuthSessionResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
