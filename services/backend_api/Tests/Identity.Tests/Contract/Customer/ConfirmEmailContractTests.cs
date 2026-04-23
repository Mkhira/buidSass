using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Identity.Persistence;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Contract.Customer;

public sealed class ConfirmEmailContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task ConfirmEmail_ValidToken_ActivatesAccount()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var registerResponse = await client.PostAsJsonAsync("/v1/customer/identity/register", ValidRegisterRequest("confirm-valid@local.dev"));
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var capture = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>();
        var emailToken = capture.RequireLatestEmailToken("confirm-valid@local.dev", "email_verification");

        var confirm = await client.PostAsJsonAsync(
            "/v1/customer/identity/email/confirm",
            new ConfirmEmailRequest(emailToken));

        confirm.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var account = await db.Accounts.SingleAsync(x => x.EmailNormalized == "confirm-valid@local.dev");
        account.Status.Should().Be("active");
        account.EmailVerifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ConfirmEmail_ExpiredToken_Returns410()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var registerResponse = await client.PostAsJsonAsync("/v1/customer/identity/register", ValidRegisterRequest("confirm-expired@local.dev"));
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var capture = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>();
        var emailToken = capture.RequireLatestEmailToken("confirm-expired@local.dev", "email_verification");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var challenge = await db.EmailVerificationChallenges
                .OrderByDescending(x => x.CreatedAt)
                .FirstAsync();
            challenge.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var confirm = await client.PostAsJsonAsync(
            "/v1/customer/identity/email/confirm",
            new ConfirmEmailRequest(emailToken));

        confirm.StatusCode.Should().Be(HttpStatusCode.Gone);
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

public sealed record RegisterAcceptedResponse(string Status);
public sealed record ConfirmEmailRequest(string Token);
