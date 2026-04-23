using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Contract.Customer;

public sealed class PasswordResetContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task PasswordResetRequest_UnknownEmail_StillReturns202()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/customer/identity/password/reset-request",
            new PasswordResetRequest("unknown@local.dev"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var capture = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>();
        capture.CountEmailDispatches("unknown@local.dev", "password_reset").Should().Be(0);
    }

    [Fact]
    public async Task PasswordResetRequest_KnownEmail_IssuesSixtyMinuteToken()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "reset-ttl@local.dev",
            password: "StrongPassword!123");

        var response = await client.PostAsJsonAsync(
            "/v1/customer/identity/password/reset-request",
            new PasswordResetRequest(seed.Email));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var token = await db.PasswordResetTokens
            .Where(x => x.AccountId == seed.AccountId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstAsync();

        var ttl = token.ExpiresAt - token.CreatedAt;
        ttl.Should().BeGreaterThan(TimeSpan.FromMinutes(59));
        ttl.Should().BeLessThan(TimeSpan.FromMinutes(61));
    }

    [Fact]
    public async Task PasswordResetComplete_ValidToken_RevokesAllSessions()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "reset-complete@local.dev",
            password: "StrongPassword!123");

        await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));

        var requestReset = await client.PostAsJsonAsync(
            "/v1/customer/identity/password/reset-request",
            new PasswordResetRequest(seed.Email));
        requestReset.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var capture = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>();
        var token = capture.RequireLatestEmailToken(seed.Email, "password_reset");
        capture.CountEmailDispatches(seed.Email, "password_reset").Should().Be(1);

        var complete = await client.PostAsJsonAsync(
            "/v1/customer/identity/password/reset-complete",
            new PasswordResetCompleteRequest(token, "NewStrongPassword!456"));

        complete.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var activeSessions = await verifyDb.Sessions
            .Where(x => x.AccountId == seed.AccountId && x.Status == "active")
            .CountAsync();

        activeSessions.Should().Be(0);

        var appDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasAudit = await appDb.AuditLogEntries
            .AnyAsync(x =>
                x.Action == "password.reset.completed"
                && x.EntityType == "Account"
                && x.EntityId == seed.AccountId);
        hasAudit.Should().BeTrue();
    }

    [Fact]
    public async Task PasswordResetComplete_ConcurrentUse_OnlySingleConsumptionSucceeds()
    {
        await factory.ResetDatabaseAsync();
        var clientA = factory.CreateClient();
        var clientB = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "reset-concurrency@local.dev",
            password: "StrongPassword!123");

        var requestReset = await clientA.PostAsJsonAsync(
            "/v1/customer/identity/password/reset-request",
            new PasswordResetRequest(seed.Email));
        requestReset.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var capture = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>();
        var token = capture.RequireLatestEmailToken(seed.Email, "password_reset");

        var payload = new PasswordResetCompleteRequest(token, "NewStrongPassword!456");
        var completeA = clientA.PostAsJsonAsync("/v1/customer/identity/password/reset-complete", payload);
        var completeB = clientB.PostAsJsonAsync("/v1/customer/identity/password/reset-complete", payload);
        await Task.WhenAll(completeA, completeB);
        var responseA = await completeA;
        var responseB = await completeB;

        var statuses = new[] { responseA.StatusCode, responseB.StatusCode };
        statuses.Should().Contain(HttpStatusCode.OK);
        statuses.Should().Contain(HttpStatusCode.BadRequest);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var pendingTokens = await verifyDb.PasswordResetTokens
            .IgnoreQueryFilters()
            .Where(x => x.AccountId == seed.AccountId && x.Status == "pending")
            .CountAsync();
        pendingTokens.Should().Be(0);
    }
}

public sealed record PasswordResetRequest(string Email);
public sealed record PasswordResetCompleteRequest(string Token, string NewPassword);
