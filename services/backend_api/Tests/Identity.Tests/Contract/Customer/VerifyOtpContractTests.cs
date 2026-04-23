using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Identity.Persistence;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Contract.Customer;

public sealed class VerifyOtpContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task VerifyOtp_ValidCode_MarksPhoneVerified()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        const string phone = "+966500000011";

        await client.PostAsJsonAsync("/v1/customer/identity/register", ValidRegisterRequest("verify-otp@local.dev", phone));

        var requestOtp = await client.PostAsJsonAsync(
            "/v1/customer/identity/otp/request",
            new RequestOtpRequest(phone, "registration_phone"));
        var otpBody = await requestOtp.Content.ReadFromJsonAsync<RequestOtpAcceptedResponse>();
        var code = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>()
            .RequireOtpCode(otpBody!.ChallengeId);

        var verify = await client.PostAsJsonAsync(
            "/v1/customer/identity/otp/verify",
            new VerifyOtpRequest(otpBody!.ChallengeId, phone, code));

        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var account = await db.Accounts.SingleAsync(x => x.EmailNormalized == "verify-otp@local.dev");
        account.PhoneVerifiedAt.Should().NotBeNull();
        var challenge = await db.OtpChallenges.SingleAsync(x => x.Id == otpBody!.ChallengeId);
        challenge.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task VerifyOtp_PasswordResetPhonePurpose_CompletesWithoutSession()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        const string phone = "+966500000013";

        await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "verify-reset@local.dev",
            password: "StrongPassword!123",
            phone: phone);

        var requestOtp = await client.PostAsJsonAsync(
            "/v1/customer/identity/otp/request",
            new RequestOtpRequest(phone, "password_reset_phone"));
        var otpBody = await requestOtp.Content.ReadFromJsonAsync<RequestOtpAcceptedResponse>();
        var code = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>()
            .RequireOtpCode(otpBody!.ChallengeId);

        var verify = await client.PostAsJsonAsync(
            "/v1/customer/identity/otp/verify",
            new VerifyOtpRequest(otpBody.ChallengeId, phone, code));

        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        verify.Content.Headers.ContentLength.Should().Be(0);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var challenge = await db.OtpChallenges.SingleAsync(x => x.Id == otpBody.ChallengeId);
        challenge.Status.Should().Be("completed");
        challenge.Attempts.Should().Be(0);
    }

    private static RegisterRequest ValidRegisterRequest(string email, string phone) =>
        new(
            Email: email,
            Phone: phone,
            Password: "StrongPassword!123",
            MarketCode: "ksa",
            Locale: "ar",
            DisplayName: "Contract User");
}

public sealed record VerifyOtpRequest(Guid ChallengeId, string Identifier, string Code);
