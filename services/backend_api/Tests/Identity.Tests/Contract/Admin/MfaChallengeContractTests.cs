using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Identity.Admin.EnrollTotp;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Contract.Admin;

public sealed class MfaChallengeContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task AdminMfaChallenge_ValidTotp_IssuesAuthSession()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "admin-mfa@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true);

        var signIn = await client.PostAsJsonAsync(
            "/v1/admin/identity/sign-in",
            new AdminSignInRequest(seed.Email, seed.Password));

        var signInBody = await signIn.Content.ReadFromJsonAsync<AdminSignInResponse>();
        var challengeRequest = new AdminMfaChallengeRequest(
            signInBody!.MfaChallenge!.ChallengeId,
            "totp",
            AdminTestDataHelper.GenerateTotpCode(seed.TotpSecret!));

        var response = await client.PostAsJsonAsync("/v1/admin/identity/mfa/challenge", challengeRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthSessionResponse>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AdminMfaChallenge_ReusedTotp_Returns409Replay()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "admin-mfa-replay@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true);

        var signIn = await client.PostAsJsonAsync(
            "/v1/admin/identity/sign-in",
            new AdminSignInRequest(seed.Email, seed.Password));

        var signInBody = await signIn.Content.ReadFromJsonAsync<AdminSignInResponse>();
        var code = AdminTestDataHelper.GenerateTotpCode(seed.TotpSecret!);
        var firstChallengeId = signInBody!.MfaChallenge!.ChallengeId;
        var firstRequest = new AdminMfaChallengeRequest(firstChallengeId, "totp", code);

        var first = await client.PostAsJsonAsync("/v1/admin/identity/mfa/challenge", firstRequest);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondSignIn = await client.PostAsJsonAsync(
            "/v1/admin/identity/sign-in",
            new AdminSignInRequest(seed.Email, seed.Password));
        secondSignIn.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondSignInBody = await secondSignIn.Content.ReadFromJsonAsync<AdminSignInResponse>();
        secondSignInBody!.MfaChallenge.Should().NotBeNull();

        var second = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/challenge",
            new AdminMfaChallengeRequest(secondSignInBody.MfaChallenge!.ChallengeId, "totp", code));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var secondBody = await second.Content.ReadFromJsonAsync<AdminMfaProblemResponse>();
        secondBody!.ReasonCode.Should().Be("identity.mfa.replay");
    }

    [Fact]
    public async Task AdminMfaChallenge_ThreeInvalidAttempts_ExhaustsChallenge()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "admin-mfa-exhaust@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true);

        var signIn = await client.PostAsJsonAsync(
            "/v1/admin/identity/sign-in",
            new AdminSignInRequest(seed.Email, seed.Password));
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);

        var signInBody = await signIn.Content.ReadFromJsonAsync<AdminSignInResponse>();
        var challengeId = signInBody!.MfaChallenge!.ChallengeId;

        for (var i = 0; i < 2; i++)
        {
            var invalid = await client.PostAsJsonAsync(
                "/v1/admin/identity/mfa/challenge",
                new AdminMfaChallengeRequest(challengeId, "totp", "000000"));

            invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var invalidBody = await invalid.Content.ReadFromJsonAsync<AdminMfaProblemResponse>();
            invalidBody!.ReasonCode.Should().Be("identity.mfa.invalid_code");
        }

        var exhausted = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/challenge",
            new AdminMfaChallengeRequest(challengeId, "totp", "000000"));

        exhausted.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var exhaustedBody = await exhausted.Content.ReadFromJsonAsync<AdminMfaProblemResponse>();
        exhaustedBody!.ReasonCode.Should().Be("identity.mfa.challenge_exhausted");

        var validAfterExhaustion = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/challenge",
            new AdminMfaChallengeRequest(challengeId, "totp", AdminTestDataHelper.GenerateTotpCode(seed.TotpSecret!)));

        validAfterExhaustion.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var validAfterExhaustionBody = await validAfterExhaustion.Content.ReadFromJsonAsync<AdminMfaProblemResponse>();
        validAfterExhaustionBody!.ReasonCode.Should().Be("identity.mfa.challenge_exhausted");
    }

    [Fact]
    public async Task AdminMfaChallenge_CorruptedSecretPayload_Returns503()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "admin-mfa-corrupt@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true);

        var signIn = await client.PostAsJsonAsync(
            "/v1/admin/identity/sign-in",
            new AdminSignInRequest(seed.Email, seed.Password));

        var signInBody = await signIn.Content.ReadFromJsonAsync<AdminSignInResponse>();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var factor = await db.AdminMfaFactors.SingleAsync(
                x => x.AccountId == seed.AccountId && x.ConfirmedAt != null && x.RevokedAt == null);
            factor.SecretEncrypted = [0x00, 0x01, 0x02, 0x03];
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/challenge",
            new AdminMfaChallengeRequest(
                signInBody!.MfaChallenge!.ChallengeId,
                "totp",
                AdminTestDataHelper.GenerateTotpCode(seed.TotpSecret!)));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<AdminMfaProblemResponse>();
        body.Should().NotBeNull();
        body!.ReasonCode.Should().Be("identity.mfa.secret_unprotect_failed");
    }

    [Fact]
    public async Task AdminMfaChallenge_RecoveryCode_IssuesAuthSessionAndConsumesCode()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        const string recoveryCode = "a1b2c3d4e5";
        var seed = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "admin-mfa-recovery@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<Argon2idHasher>();
            var factor = await db.AdminMfaFactors.SingleAsync(
                x => x.AccountId == seed.AccountId && x.ConfirmedAt != null && x.RevokedAt == null);
            factor.RecoveryCodesHash = JsonSerializer.Serialize(
                new[] { new RecoveryCodeHashPayload(hasher.HashPassword(recoveryCode, SurfaceKind.Admin), null) });
            await db.SaveChangesAsync();
        }

        var signIn = await client.PostAsJsonAsync(
            "/v1/admin/identity/sign-in",
            new AdminSignInRequest(seed.Email, seed.Password));
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);

        var signInBody = await signIn.Content.ReadFromJsonAsync<AdminSignInResponse>();
        var response = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/challenge",
            new AdminMfaChallengeRequest(signInBody!.MfaChallenge!.ChallengeId, "recovery_code", recoveryCode));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthSessionResponse>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var verifyFactor = await verifyDb.AdminMfaFactors.SingleAsync(
            x => x.AccountId == seed.AccountId && x.ConfirmedAt != null && x.RevokedAt == null);
        var payloads = JsonSerializer.Deserialize<List<RecoveryCodeHashPayload>>(verifyFactor.RecoveryCodesHash);
        payloads.Should().NotBeNull();
        payloads![0].UsedAt.Should().NotBeNull();

        var appDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasRecoveryAudit = await appDb.AuditLogEntries.AnyAsync(x =>
            x.Action == "admin.mfa.recovery_code_consumed"
            && x.EntityType == "AdminMfaFactor"
            && x.EntityId == verifyFactor.Id);
        hasRecoveryAudit.Should().BeTrue();
    }
}

public sealed record AdminMfaProblemResponse(string? ReasonCode);
