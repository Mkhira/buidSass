using System.Net;
using System.Net.Http.Json;
using System.Web;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;

namespace Identity.Tests.Contract.Admin;

public sealed class TotpEnrollContractTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task AdminTotpEnroll_InvitationFlow_CompletesSetup()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var invitationToken = "invitation-token-1";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var role = new Role
            {
                Id = Guid.NewGuid(),
                Code = "platform.finance",
                NameAr = "مالية",
                NameEn = "Finance",
                Scope = "platform",
                System = true,
            };
            db.Roles.Add(role);

            db.AdminInvitations.Add(new AdminInvitation
            {
                Id = Guid.NewGuid(),
                EmailNormalized = "invite-admin@local.dev",
                InvitedByAccountId = Guid.NewGuid(),
                InvitedRoleId = role.Id,
                TokenHash = AdminTestDataHelper.HashToken(invitationToken),
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(72),
                Status = "pending",
            });

            await db.SaveChangesAsync();
        }

        var accept = await client.PostAsJsonAsync(
            "/v1/admin/identity/invitation/accept",
            new AcceptInvitationRequest(invitationToken, "AdminOnly!12345"));
        accept.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var acceptBody = await accept.Content.ReadFromJsonAsync<AcceptInvitationResponse>();
        acceptBody.Should().NotBeNull();
        acceptBody!.PartialAuthToken.Should().NotBeNullOrWhiteSpace();

        var enroll = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/totp/enroll",
            new EnrollTotpRequest(acceptBody.PartialAuthToken));

        enroll.StatusCode.Should().Be(HttpStatusCode.OK);
        var enrollBody = await enroll.Content.ReadFromJsonAsync<EnrollTotpResponse>();
        enrollBody.Should().NotBeNull();
        enrollBody!.FactorId.Should().NotBe(Guid.Empty);
        enrollBody.RecoveryCodes.Should().HaveCount(10);
        enrollBody.OtpauthUri.Should().Contain("otpauth://totp/");

        var secret = ExtractSecretFromOtpAuthUri(enrollBody.OtpauthUri);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(DateTime.UtcNow);

        var confirm = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/totp/confirm",
            new ConfirmTotpRequest(acceptBody.PartialAuthToken, enrollBody.FactorId, code));

        confirm.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var factor = await verifyDb.AdminMfaFactors.SingleAsync(x => x.Id == enrollBody.FactorId);
        factor.ConfirmedAt.Should().NotBeNull();
    }

    private static string ExtractSecretFromOtpAuthUri(string otpauthUri)
    {
        var uri = new Uri(otpauthUri);
        var query = HttpUtility.ParseQueryString(uri.Query);
        return query["secret"]!;
    }
}

public sealed record AcceptInvitationRequest(string Token, string NewPassword);
public sealed record AcceptInvitationResponse(string PartialAuthToken);
public sealed record EnrollTotpRequest(string PartialAuthToken);
public sealed record EnrollTotpResponse(Guid FactorId, string OtpauthUri, IReadOnlyList<string> RecoveryCodes);
public sealed record ConfirmTotpRequest(string PartialAuthToken, Guid FactorId, string Code);
