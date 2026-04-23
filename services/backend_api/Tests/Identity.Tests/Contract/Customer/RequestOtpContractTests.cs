using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Identity.Persistence;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Contract.Customer;

public sealed class RequestOtpContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task RequestOtp_RegistrationPhone_DispatchesChallenge()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/customer/identity/otp/request",
            new RequestOtpRequest("+966500000001", "registration_phone"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<RequestOtpAcceptedResponse>();
        body.Should().NotBeNull();
        body!.ChallengeId.Should().NotBe(Guid.Empty);
        var rawBody = await response.Content.ReadAsStringAsync();
        rawBody.Should().NotContainEquivalentOf("devCode");
        rawBody.Should().NotContainEquivalentOf("otpCode");
        rawBody.Should().NotMatchRegex(@":\s*""\d{6,8}""");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var challenge = await db.OtpChallenges.SingleAsync(x => x.Id == body.ChallengeId);
        challenge.Status.Should().Be("pending");
        challenge.Purpose.Should().Be("registration_phone");

        var capture = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>();
        var code = capture.RequireOtpCode(body.ChallengeId);
        code.Should().MatchRegex(@"^\d{6}$");
    }

    [Fact]
    public async Task RequestOtp_NewChallenge_SupersedesPriorPendingChallenge()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync(
            "/v1/customer/identity/otp/request",
            new RequestOtpRequest("+966500000002", "registration_phone"));
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var firstBody = await first.Content.ReadFromJsonAsync<RequestOtpAcceptedResponse>();

        var second = await client.PostAsJsonAsync(
            "/v1/customer/identity/otp/request",
            new RequestOtpRequest("+966500000002", "registration_phone"));
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var secondBody = await second.Content.ReadFromJsonAsync<RequestOtpAcceptedResponse>();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var firstChallenge = await db.OtpChallenges.SingleAsync(x => x.Id == firstBody!.ChallengeId);
        var secondChallenge = await db.OtpChallenges.SingleAsync(x => x.Id == secondBody!.ChallengeId);

        firstChallenge.Status.Should().Be("superseded");
        firstChallenge.CompletedAt.Should().NotBeNull();
        secondChallenge.Status.Should().Be("pending");
    }

    [Fact]
    public async Task RequestOtp_PerIdentifierRateLimit_Returns429()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/v1/customer/identity/otp/request",
                new RequestOtpRequest("+966500000003", "registration_phone"));
            response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        var blocked = await client.PostAsJsonAsync(
            "/v1/customer/identity/otp/request",
            new RequestOtpRequest("+966500000003", "registration_phone"));

        blocked.StatusCode.Should().Be((HttpStatusCode)429);
    }
}

public sealed record RequestOtpRequest(string Phone, string Purpose);
public sealed record RequestOtpAcceptedResponse(Guid ChallengeId);
