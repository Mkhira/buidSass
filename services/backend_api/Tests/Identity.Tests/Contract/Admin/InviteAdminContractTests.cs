using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Contract.Admin;

public sealed class InviteAdminContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task InviteAdmin_SuperAdminWithStepUp_Returns202()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var superAdmin = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "invite-super@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true,
            roleCode: "platform.super_admin");

        var steppedUpToken = await SignInAndStepUpAsync(factory, client, superAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", steppedUpToken);
        await EnsureRoleExistsAsync(factory, "platform.finance");

        var response = await client.PostAsJsonAsync(
            "/v1/admin/identity/invitations",
            new InviteAdminRequest("new-admin@local.dev", "platform.finance"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var capture = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>();
        capture.CountEmailDispatches("new-admin@local.dev", "admin_invitation").Should().Be(1);
    }

    [Fact]
    public async Task InviteAdmin_NonSuperAdmin_Returns403()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var supportAdmin = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "invite-support@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true,
            roleCode: "platform.support");

        var steppedUpToken = await SignInAndStepUpAsync(factory, client, supportAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", steppedUpToken);
        await EnsureRoleExistsAsync(factory, "platform.finance");

        var response = await client.PostAsJsonAsync(
            "/v1/admin/identity/invitations",
            new InviteAdminRequest("blocked-admin@local.dev", "platform.finance"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task InviteAdmin_CustomerScopedRole_Returns400InvalidRoleScope()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var superAdmin = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "invite-scope-super@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true,
            roleCode: "platform.super_admin");

        var steppedUpToken = await SignInAndStepUpAsync(factory, client, superAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", steppedUpToken);

        await EnsureRoleExistsAsync(factory, "customer.standard", roleScope: "customer", system: true);
        var response = await client.PostAsJsonAsync(
            "/v1/admin/identity/invitations",
            new InviteAdminRequest("invalid-scope@local.dev", "customer.standard"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<InviteProblemResponse>();
        body!.ReasonCode.Should().Be("identity.invitation.invalid_role_scope");
    }

    [Fact]
    public async Task InviteAdmin_PendingInvitationExists_Returns409()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var superAdmin = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "invite-dup-super@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true,
            roleCode: "platform.super_admin");

        var steppedUpToken = await SignInAndStepUpAsync(factory, client, superAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", steppedUpToken);
        await EnsureRoleExistsAsync(factory, "platform.finance");

        const string invitedEmail = "duplicate-admin@local.dev";
        _ = await CreateInvitationAsync(factory, superAdmin.AccountId, invitedEmail);

        var response = await client.PostAsJsonAsync(
            "/v1/admin/identity/invitations",
            new InviteAdminRequest(invitedEmail, "platform.finance"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<InviteProblemResponse>();
        body!.ReasonCode.Should().Be("identity.invitation.pending_exists");

        var capture = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>();
        capture.CountEmailDispatches(invitedEmail, "admin_invitation").Should().Be(0);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var invites = await db.AdminInvitations
            .Where(x => x.EmailNormalized == invitedEmail && x.Status == "pending")
            .CountAsync();
        invites.Should().Be(1);
    }

    [Fact]
    public async Task RevokeInvitation_SuperAdmin_Succeeds()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var superAdmin = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "revoke-super@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true,
            roleCode: "platform.super_admin");

        var steppedUpToken = await SignInAndStepUpAsync(factory, client, superAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", steppedUpToken);

        var invitationId = await CreateInvitationAsync(factory, superAdmin.AccountId, "pending-admin@local.dev");

        var response = await client.DeleteAsync($"/v1/admin/identity/invitations/{invitationId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
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
            new InviteStepUpRequest("admin_invitation"));
        stepUp.EnsureSuccessStatusCode();
        var stepUpBody = await stepUp.Content.ReadFromJsonAsync<InviteStepUpAcceptedResponse>();
        var otpCode = factory.Services.GetRequiredService<IdentityDispatchCaptureStore>()
            .RequireOtpCode(stepUpBody!.ChallengeId);

        var confirm = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/step-up/confirm",
            new InviteStepUpConfirmRequest(stepUpBody!.ChallengeId, otpCode));
        confirm.EnsureSuccessStatusCode();

        var confirmBody = await confirm.Content.ReadFromJsonAsync<InviteStepUpConfirmResponse>();
        return confirmBody!.AccessToken;
    }

    private static async Task<Guid> CreateInvitationAsync(IdentityTestFactory factory, Guid invitedByAccountId, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var role = await EnsureRoleExistsInternalAsync(db, "platform.finance");

        var invitation = new AdminInvitation
        {
            Id = Guid.NewGuid(),
            EmailNormalized = email.ToLowerInvariant(),
            InvitedByAccountId = invitedByAccountId,
            InvitedRoleId = role.Id,
            TokenHash = AdminTestDataHelper.HashToken(Guid.NewGuid().ToString("N")),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(2),
            Status = "pending",
        };

        db.AdminInvitations.Add(invitation);
        await db.SaveChangesAsync();
        return invitation.Id;
    }

    private static async Task EnsureRoleExistsAsync(
        IdentityTestFactory factory,
        string roleCode,
        string roleScope = "platform",
        bool system = true)
    {
        await using var asyncScope = factory.Services.CreateAsyncScope();
        var db = asyncScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        _ = await EnsureRoleExistsInternalAsync(db, roleCode, roleScope, system);
    }

    private static async Task<Role> EnsureRoleExistsInternalAsync(
        IdentityDbContext db,
        string roleCode,
        string scope = "platform",
        bool system = true)
    {
        var role = await db.Roles.SingleOrDefaultAsync(x => x.Code == roleCode);
        if (role is not null)
        {
            return role;
        }

        role = new Role
        {
            Id = Guid.NewGuid(),
            Code = roleCode,
            NameAr = roleCode,
            NameEn = roleCode,
            Scope = scope,
            System = system,
        };

        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role;
    }
}

public sealed record InviteAdminRequest(string Email, string RoleCode);
public sealed record InviteProblemResponse(string? ReasonCode);
public sealed record InviteStepUpRequest(string Purpose);
public sealed record InviteStepUpAcceptedResponse(Guid ChallengeId);
public sealed record InviteStepUpConfirmRequest(Guid ChallengeId, string Code);
public sealed record InviteStepUpConfirmResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset StepUpValidUntil);
