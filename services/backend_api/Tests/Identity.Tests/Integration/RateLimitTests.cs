using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Identity.Customer.SignIn;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using FluentAssertions;
using Identity.Tests.Contract.Customer;
using Identity.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Integration;

public sealed class RateLimitTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task RateLimit_CustomerSignIn_BlocksAfterThreshold()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        HttpStatusCode finalStatus = HttpStatusCode.OK;
        for (var i = 0; i < 22; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/v1/customer/identity/sign-in",
                new CustomerSignInRequest("unknown-rate-limit@local.dev", "wrong-password"));
            finalStatus = response.StatusCode;
        }

        finalStatus.Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task RateLimit_CustomerSignIn_RejectionWritesRateLimitEvent()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        for (var i = 0; i < 22; i++)
        {
            await client.PostAsJsonAsync(
                "/v1/customer/identity/sign-in",
                new CustomerSignInRequest("rate-limit-audit@local.dev", "wrong-password"));
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var latest = await db.RateLimitEvents
            .OrderByDescending(x => x.BlockedAt)
            .FirstOrDefaultAsync();

        latest.Should().NotBeNull();
        latest!.PolicyCode.Should().Be(RateLimitPolicies.CustomerSignIn);
        latest.Surface.Should().Be("customer");
        latest.ScopeKeyHash.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Lockout_AfterThresholdFailures_ReleasesAfterWindow()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "lockout@local.dev",
            password: "StrongPassword!123");

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync(
                "/v1/customer/identity/sign-in",
                new CustomerSignInRequest(seed.Email, "wrong-password"));
        }

        var locked = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));

        locked.StatusCode.Should().Be((HttpStatusCode)423);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var lockout = await db.LockoutStates.SingleAsync(x => x.AccountId == seed.AccountId && x.Reason == "signin");
            lockout.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
            lockout.FirstFailedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
            await db.SaveChangesAsync();
        }

        var released = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new CustomerSignInRequest(seed.Email, seed.Password));

        released.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Lockout_ProgressesAcrossTiers_ThenRequiresAdminUnlock()
    {
        await factory.ResetDatabaseAsync();

        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "lockout-progressive@local.dev",
            password: "StrongPassword!123");

        for (var expectedTier = 1; expectedTier <= 3; expectedTier++)
        {
            for (var i = 0; i < 5; i++)
            {
                var failed = await InvokeSignInHandlerAsync(seed.Email, "wrong-password");
                failed.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            }

            var locked = await InvokeSignInHandlerAsync(seed.Email, seed.Password);
            locked.StatusCode.Should().Be(StatusCodes.Status423Locked);
            locked.ReasonCode.Should().Be("identity.lockout.active");
            locked.LockedUntil.Should().NotBeNull();

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var lockout = await db.LockoutStates.SingleAsync(x => x.AccountId == seed.AccountId && x.Reason == "signin");
            lockout.Tier.Should().Be(expectedTier);
            lockout.CooldownIndex.Should().Be(expectedTier);
            lockout.RequiresAdminUnlock.Should().BeFalse();
            lockout.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
            lockout.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        for (var i = 0; i < 5; i++)
        {
            var failed = await InvokeSignInHandlerAsync(seed.Email, "wrong-password");
            failed.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        }

        var adminUnlockLocked = await InvokeSignInHandlerAsync(seed.Email, seed.Password);
        adminUnlockLocked.StatusCode.Should().Be(StatusCodes.Status423Locked);
        adminUnlockLocked.ReasonCode.Should().Be("identity.lockout.admin_unlock_required");
        adminUnlockLocked.LockedUntil.Should().BeNull();

        await using (var finalScope = factory.Services.CreateAsyncScope())
        {
            var db = finalScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var lockout = await db.LockoutStates.SingleAsync(x => x.AccountId == seed.AccountId && x.Reason == "signin");
            lockout.Tier.Should().Be(4);
            lockout.RequiresAdminUnlock.Should().BeTrue();
            lockout.LockedUntil.Should().BeNull();
        }

        async Task<CustomerSignInHandlerResult> InvokeSignInHandlerAsync(string identifier, string password)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Identity.Primitives.Argon2idHasher>();
            var authSessionService = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Identity.Customer.Common.CustomerAuthSessionService>();
            var auditEventPublisher = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.AuditLog.IAuditEventPublisher>();
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers.UserAgent = "identity-rate-limit-tests";
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");

            return await CustomerSignInHandler.HandleAsync(
                new BackendApi.Modules.Identity.Customer.SignIn.CustomerSignInRequest(identifier, password),
                httpContext,
                db,
                hasher,
                authSessionService,
                auditEventPublisher,
                CancellationToken.None);
        }
    }
}
