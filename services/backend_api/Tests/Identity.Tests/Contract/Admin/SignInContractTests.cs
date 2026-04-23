using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Identity.Persistence;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Contract.Admin;

public sealed class SignInContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task AdminSignIn_ValidCredentials_RequiresMfa()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "admin-valid@local.dev",
            password: "AdminOnly!12345",
            permissions: [],
            withTotpFactor: true);

        var response = await client.PostAsJsonAsync(
            "/v1/admin/identity/sign-in",
            new AdminSignInRequest(seed.Email, seed.Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AdminSignInResponse>();
        body.Should().NotBeNull();
        body!.MfaChallenge.Should().NotBeNull();
        body.MfaChallenge!.Kind.Should().Be("totp");
        body.MfaChallenge.ChallengeId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task AdminSignIn_RequiredRoleWithoutTotp_Returns412EnrollmentRequired()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "admin-no-mfa@local.dev",
            password: "AdminOnly!12345",
            permissions: [],
            withTotpFactor: false,
            roleCode: "platform.super_admin");

        var response = await client.PostAsJsonAsync(
            "/v1/admin/identity/sign-in",
            new AdminSignInRequest(seed.Email, seed.Password));

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        var body = await response.Content.ReadFromJsonAsync<AdminSignInProblemResponse>();
        body.Should().NotBeNull();
        body!.ReasonCode.Should().Be("identity.mfa.enrollment_required");
        body.EnrollmentPath.Should().Be("/v1/admin/identity/mfa/totp/enroll");
    }

    [Fact]
    public async Task AdminSignIn_NonRequiredRoleWithoutTotp_IssuesSessionWithoutMfaChallenge()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "admin-support@local.dev",
            password: "AdminOnly!12345",
            permissions: [],
            withTotpFactor: false,
            roleCode: "platform.support");

        var response = await client.PostAsJsonAsync(
            "/v1/admin/identity/sign-in",
            new AdminSignInRequest(seed.Email, seed.Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AdminSignInResponse>();
        body.Should().NotBeNull();
        body!.MfaChallenge.Should().BeNull();
        body.AuthSession.Should().NotBeNull();
        body.AuthSession!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AdminSignIn_InvalidCredentials_Returns400UniformCopy()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "admin-invalid@local.dev",
            password: "AdminOnly!12345",
            permissions: [],
            withTotpFactor: true);

        var wrongPassword = await client.PostAsJsonAsync(
            "/v1/admin/identity/sign-in",
            new AdminSignInRequest("admin-invalid@local.dev", "wrong-password"));

        var unknownUser = await client.PostAsJsonAsync(
            "/v1/admin/identity/sign-in",
            new AdminSignInRequest("unknown-admin@local.dev", "wrong-password"));

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unknownUser.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var wrongBody = await wrongPassword.Content.ReadFromJsonAsync<AdminSignInProblemResponse>();
        var unknownBody = await unknownUser.Content.ReadFromJsonAsync<AdminSignInProblemResponse>();
        wrongBody!.ReasonCode.Should().Be("identity.sign_in.invalid_credentials");
        unknownBody!.ReasonCode.Should().Be("identity.sign_in.invalid_credentials");
    }

    [Fact]
    public async Task AdminSignIn_DisabledAccount_Returns403()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "admin-disabled@local.dev",
            password: "AdminOnly!12345",
            permissions: [],
            withTotpFactor: false,
            roleCode: "platform.support");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var account = await db.Accounts.SingleAsync(x => x.Id == seed.AccountId);
            account.Status = "disabled";
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/v1/admin/identity/sign-in",
            new AdminSignInRequest(seed.Email, seed.Password));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<AdminSignInProblemResponse>();
        body!.ReasonCode.Should().Be("identity.account.disabled");
    }
}

public sealed record AdminSignInProblemResponse(string? ReasonCode, string? EnrollmentPath);
