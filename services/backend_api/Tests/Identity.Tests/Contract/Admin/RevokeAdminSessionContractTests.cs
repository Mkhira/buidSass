using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Contract.Admin;

public sealed class RevokeAdminSessionContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task AdminRevokeAdminSession_SuperAdminWithStepUp_Succeeds()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var superAdmin = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "super-admin@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true,
            roleCode: "platform.super_admin");

        var targetAdmin = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "target-admin@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true,
            roleCode: "platform.support");

        var superToken = await AdminTestDataHelper.SignInAndCompleteMfaAsync(
            client,
            superAdmin.Email,
            superAdmin.Password,
            superAdmin.TotpSecret!);

        var targetToken = await AdminTestDataHelper.SignInAndCompleteMfaAsync(
            client,
            targetAdmin.Email,
            targetAdmin.Password,
            targetAdmin.TotpSecret!);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superToken);

        var stepUp = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/step-up",
            new StepUpRequest("admin_revoke_session"));
        stepUp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var stepUpBody = await stepUp.Content.ReadFromJsonAsync<StepUpAcceptedResponse>();
        var otpCode = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>()
            .RequireOtpCode(stepUpBody!.ChallengeId);

        var confirm = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/step-up/confirm",
            new StepUpConfirmRequest(stepUpBody!.ChallengeId, otpCode));
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmBody = await confirm.Content.ReadFromJsonAsync<StepUpConfirmResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", confirmBody!.AccessToken);

        var targetSessionId = ParseSessionId(targetToken);
        var revoke = await client.DeleteAsync($"/v1/admin/identity/accounts/{targetAdmin.AccountId}/sessions/{targetSessionId}");

        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static Guid ParseSessionId(string jwt)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        var sid = token.Claims.Single(x => x.Type == "sid").Value;
        return Guid.Parse(sid);
    }
}

public sealed record StepUpRequest(string Purpose);
public sealed record StepUpAcceptedResponse(Guid ChallengeId);
public sealed record StepUpConfirmRequest(Guid ChallengeId, string Code);
public sealed record StepUpConfirmResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset StepUpValidUntil);
