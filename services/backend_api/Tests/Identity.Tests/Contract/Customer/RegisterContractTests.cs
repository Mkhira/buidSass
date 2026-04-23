using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Identity.Persistence;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Contract.Customer;

public sealed class RegisterContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task Register_EmitsAcceptedAndPendingVerification()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/customer/identity/register", ValidRegisterRequest("contract-1@local.dev"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<RegisterAcceptedEnvelope>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("pending_email_verification");
        var rawBody = await response.Content.ReadAsStringAsync();
        rawBody.Should().NotContainEquivalentOf("token");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var account = await db.Accounts.SingleAsync(x => x.EmailNormalized == "contract-1@local.dev");
        account.Status.Should().Be("pending_email_verification");
        var challenge = await db.EmailVerificationChallenges.SingleAsync(x => x.AccountId == account.Id);
        var ttl = challenge.ExpiresAt - challenge.CreatedAt;
        ttl.Should().BeGreaterThan(TimeSpan.FromHours(23));
        ttl.Should().BeLessThan(TimeSpan.FromHours(25));

        var capture = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>();
        capture.CountEmailDispatches("contract-1@local.dev", "email_verification").Should().Be(1);
    }

    [Fact]
    public async Task Register_DuplicateEmail_StillReturnsAccepted()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/v1/customer/identity/register", ValidRegisterRequest("duplicate@local.dev"));
        var second = await client.PostAsJsonAsync("/v1/customer/identity/register", ValidRegisterRequest("duplicate@local.dev"));

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var capture = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>();
        capture.CountEmailDispatches("duplicate@local.dev", "email_verification").Should().Be(1);
    }

    [Fact]
    public async Task Register_PhoneMarketMismatch_Returns400()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var request = ValidRegisterRequest("mismatch@local.dev") with
        {
            Phone = "+201012345678",
            MarketCode = "ksa",
        };

        var response = await client.PostAsJsonAsync("/v1/customer/identity/register", request);
        var body = await response.Content.ReadFromJsonAsync<ProblemResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().NotBeNull();
        body!.ReasonCode.Should().Be("identity.phone.market_mismatch");
    }

    private static RegisterRequest ValidRegisterRequest(string email) =>
        new(
            Email: email,
            Phone: "+966501234567",
            Password: "StrongPassword!123",
            MarketCode: "ksa",
            Locale: "ar",
            DisplayName: "Contract User");
}

public sealed record RegisterRequest(
    string Email,
    string Phone,
    string Password,
    string MarketCode,
    string Locale,
    string DisplayName);

public sealed record RegisterAcceptedEnvelope(string Status);

public sealed record ProblemResponse(string? ReasonCode);
