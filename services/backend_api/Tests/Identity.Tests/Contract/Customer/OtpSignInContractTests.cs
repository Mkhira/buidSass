using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Contract.Customer;

public sealed class OtpSignInContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task OtpSignIn_VerifiedCode_IssuesSession()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        const string phone = "+966500000012";

        await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "otp-signin@local.dev",
            password: "StrongPassword!123",
            phone: phone);

        var requestOtp = await client.PostAsJsonAsync(
            "/v1/customer/identity/otp/request",
            new RequestOtpRequest(phone, "signin_customer"));

        requestOtp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var otpBody = await requestOtp.Content.ReadFromJsonAsync<RequestOtpAcceptedResponse>();
        var code = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>()
            .RequireOtpCode(otpBody!.ChallengeId);

        var verify = await client.PostAsJsonAsync(
            "/v1/customer/identity/otp/verify",
            new VerifyOtpRequest(otpBody!.ChallengeId, phone, code));

        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await verify.Content.ReadFromJsonAsync<AuthSessionResponse>();
        session.Should().NotBeNull();
        session!.AccessToken.Should().NotBeNullOrWhiteSpace();
        session.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }
}
