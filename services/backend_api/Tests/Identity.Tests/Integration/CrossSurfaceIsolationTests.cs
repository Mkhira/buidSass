using System.Net;
using FluentAssertions;
using Identity.Tests.Infrastructure;

namespace Identity.Tests.Integration;

public sealed class CrossSurfaceIsolationTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task AdminToken_RejectedOnCustomerSurface()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "admin-surface@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true);

        var adminToken = await AdminTestDataHelper.SignInAndCompleteMfaAsync(
            client,
            seed.Email,
            seed.Password,
            seed.TotpSecret!);

        AdminTestDataHelper.SetBearer(client, adminToken);
        var response = await client.GetAsync("/v1/customer/identity/_test/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
