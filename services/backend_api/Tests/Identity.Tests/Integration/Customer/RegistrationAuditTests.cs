using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Identity.Tests.Contract.Customer;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Integration.Customer;

public sealed class RegistrationAuditTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task RegistrationLifecycle_EmitsAuditEvents_WithCorrelationId()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var correlationId = Guid.NewGuid();
        const string phone = "+966500000013";

        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId.ToString("D"));

        var register = await client.PostAsJsonAsync("/v1/customer/identity/register", ValidRegisterRequest("audit-us1@local.dev", phone));
        register.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var capture = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>();
        var emailToken = capture.RequireLatestEmailToken("audit-us1@local.dev", "email_verification");

        var confirm = await client.PostAsJsonAsync(
            "/v1/customer/identity/email/confirm",
            new ConfirmEmailRequest(emailToken));
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);

        var requestOtp = await client.PostAsJsonAsync(
            "/v1/customer/identity/otp/request",
            new RequestOtpRequest(phone, "registration_phone"));
        var otpBody = await requestOtp.Content.ReadFromJsonAsync<RequestOtpAcceptedResponse>();
        var otpCode = capture.RequireOtpCode(otpBody!.ChallengeId);

        var verify = await client.PostAsJsonAsync(
            "/v1/customer/identity/otp/verify",
            new VerifyOtpRequest(otpBody!.ChallengeId, phone, otpCode));
        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var actions = await appDb.AuditLogEntries
            .Where(x => x.CorrelationId == correlationId)
            .Select(x => x.Action)
            .ToListAsync();

        actions.Should().Contain("account.created");
        actions.Should().Contain("email.verified");
        actions.Should().Contain("phone.verified");
    }

    private static RegisterRequest ValidRegisterRequest(string email, string phone) =>
        new(
            Email: email,
            Phone: phone,
            Password: "StrongPassword!123",
            MarketCode: "ksa",
            Locale: "ar",
            DisplayName: "Audit User");
}
