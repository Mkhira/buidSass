using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Identity.Persistence;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Contract.Customer;

public sealed class SignInContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task CustomerSignIn_ValidCredentials_IssuesSession()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "signin-valid@local.dev",
            password: "StrongPassword!123");

        var response = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthSessionResponse>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var sessions = await db.Sessions.Where(x => x.AccountId == seed.AccountId).ToListAsync();
        sessions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CustomerSignIn_WithPhoneIdentifier_IssuesSession()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "signin-phone@local.dev",
            password: "StrongPassword!123");

        var response = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest("+966501234567", seed.Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthSessionResponse>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CustomerSignIn_InvalidCredentials_UniformError()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "signin-invalid@local.dev",
            password: "StrongPassword!123");

        var wrongPassword = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest("signin-invalid@local.dev", "wrong-password"));

        var unknownUser = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest("unknown@local.dev", "wrong-password"));

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unknownUser.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var wrongBody = await wrongPassword.Content.ReadFromJsonAsync<CustomerSignInProblemResponse>();
        var unknownBody = await unknownUser.Content.ReadFromJsonAsync<CustomerSignInProblemResponse>();
        wrongBody!.ReasonCode.Should().Be("identity.sign_in.invalid_credentials");
        unknownBody!.ReasonCode.Should().Be("identity.sign_in.invalid_credentials");
    }

    [Fact]
    public async Task CustomerSignIn_LockedAccount_Returns423WithLockedUntil()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "signin-locked@local.dev",
            password: "StrongPassword!123");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.LockoutStates.Add(new BackendApi.Modules.Identity.Entities.LockoutState
            {
                AccountId = seed.AccountId,
                Reason = "signin",
                FailedCount = 5,
                FirstFailedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                LockedUntil = DateTimeOffset.UtcNow.AddMinutes(10),
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));

        response.StatusCode.Should().Be((HttpStatusCode)423);
        var body = await response.Content.ReadFromJsonAsync<CustomerSignInProblemResponse>();
        body.Should().NotBeNull();
        body!.ReasonCode.Should().Be("identity.lockout.active");
        body.LockedUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task CustomerSignIn_PendingEmailVerification_Returns403()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "signin-pending-email@local.dev",
            password: "StrongPassword!123");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var account = await db.Accounts.SingleAsync(x => x.Id == seed.AccountId);
            account.Status = "pending_email_verification";
            account.EmailVerifiedAt = null;
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<CustomerSignInProblemResponse>();
        body!.ReasonCode.Should().Be("identity.account.pending_email_verification");
    }
}

public sealed record CustomerSignInRequest(string Identifier, string Password);
public sealed record AuthSessionResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
public sealed record CustomerSignInProblemResponse(string? ReasonCode, DateTimeOffset? LockedUntil);
