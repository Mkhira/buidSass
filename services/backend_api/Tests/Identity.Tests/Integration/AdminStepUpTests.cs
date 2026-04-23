using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Integration;

public sealed class AdminStepUpTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task AdminStepUpOtp_RequiredForSensitiveOps()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "admin-stepup@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true);

        var token = await AdminTestDataHelper.SignInAndCompleteMfaAsync(
            client,
            seed.Email,
            seed.Password,
            seed.TotpSecret!);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var withoutStepUp = await client.GetAsync("/v1/admin/identity/_test/step-up-protected");
        withoutStepUp.StatusCode.Should().Be((HttpStatusCode)412);

        var stepUp = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/step-up",
            new StepUpRequest("admin_sensitive_operation"));

        stepUp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var stepUpBody = await stepUp.Content.ReadFromJsonAsync<StepUpAcceptedResponse>();
        var code = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>()
            .RequireOtpCode(stepUpBody!.ChallengeId);

        var confirm = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/step-up/confirm",
            new StepUpConfirmRequest(stepUpBody!.ChallengeId, code));

        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmBody = await confirm.Content.ReadFromJsonAsync<StepUpConfirmResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", confirmBody!.AccessToken);
        var withStepUp = await client.GetAsync("/v1/admin/identity/_test/step-up-protected");
        withStepUp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

public sealed record StepUpRequest(string Purpose);
public sealed record StepUpAcceptedResponse(Guid ChallengeId);
public sealed record StepUpConfirmRequest(Guid ChallengeId, string Code);
public sealed record StepUpConfirmResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset StepUpValidUntil);
