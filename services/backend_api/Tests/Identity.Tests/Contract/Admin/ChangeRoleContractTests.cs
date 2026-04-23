using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Contract.Admin;

public sealed class ChangeRoleContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task ChangeAdminRole_SuperAdminWithStepUp_AuditsBeforeAndAfter()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var superAdmin = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "change-role-super@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true,
            roleCode: "platform.super_admin");

        var targetAdmin = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "change-role-target@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true,
            roleCode: "platform.support");

        var steppedUpToken = await SignInAndStepUpAsync(factory, client, superAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", steppedUpToken);
        await EnsureRoleExistsAsync(factory, "platform.finance");

        var change = await client.PatchAsJsonAsync(
            $"/v1/admin/identity/accounts/{targetAdmin.AccountId}/role",
            new ChangeRoleRequest("platform.finance", "platform"));

        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = factory.Services.CreateAsyncScope();
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await appDb.AuditLogEntries
            .Where(x => x.Action == "identity.admin.role.change.before" || x.Action == "identity.admin.role.change.after")
            .ToListAsync();

        rows.Count.Should().BeGreaterOrEqualTo(2);
    }

    private static async Task<string> SignInAndStepUpAsync(IdentityTestFactory factory, HttpClient client, AdminSeedResult admin)
    {
        var token = await AdminTestDataHelper.SignInAndCompleteMfaAsync(
            client,
            admin.Email,
            admin.Password,
            admin.TotpSecret!);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var stepUp = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/step-up",
            new ChangeRoleStepUpRequest("admin_change_role"));
        stepUp.EnsureSuccessStatusCode();
        var stepUpBody = await stepUp.Content.ReadFromJsonAsync<ChangeRoleStepUpAcceptedResponse>();
        var otpCode = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>()
            .RequireOtpCode(stepUpBody!.ChallengeId);

        var confirm = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/step-up/confirm",
            new ChangeRoleStepUpConfirmRequest(stepUpBody!.ChallengeId, otpCode));
        confirm.EnsureSuccessStatusCode();

        var confirmBody = await confirm.Content.ReadFromJsonAsync<ChangeRoleStepUpConfirmResponse>();
        return confirmBody!.AccessToken;
    }

    private static async Task EnsureRoleExistsAsync(IdentityTestFactory factory, string roleCode)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var existing = await db.Roles.SingleOrDefaultAsync(x => x.Code == roleCode);
        if (existing is not null)
        {
            return;
        }

        db.Roles.Add(new BackendApi.Modules.Identity.Entities.Role
        {
            Id = Guid.NewGuid(),
            Code = roleCode,
            NameAr = roleCode,
            NameEn = roleCode,
            Scope = "platform",
            System = true,
        });
        await db.SaveChangesAsync();
    }
}

public sealed record ChangeRoleRequest(string RoleCode, string MarketCode);
public sealed record ChangeRoleStepUpRequest(string Purpose);
public sealed record ChangeRoleStepUpAcceptedResponse(Guid ChallengeId);
public sealed record ChangeRoleStepUpConfirmRequest(Guid ChallengeId, string Code);
public sealed record ChangeRoleStepUpConfirmResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset StepUpValidUntil);
