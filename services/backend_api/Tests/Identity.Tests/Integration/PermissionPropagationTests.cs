using System.Net;
using System.Net.Http.Headers;
using BackendApi.Modules.Identity.Persistence;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Integration;

public sealed class PermissionPropagationTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task RolePermissionChange_AppliesOnNextRequest_WithExistingAccessToken()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var admin = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "permission-propagation@local.dev",
            password: "AdminOnly!12345",
            permissions: ["identity.admin.session.manage"],
            withTotpFactor: true,
            roleCode: "platform.support");

        var token = await AdminTestDataHelper.SignInAndCompleteMfaAsync(
            client,
            admin.Email,
            admin.Password,
            admin.TotpSecret!);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var beforeChange = await client.GetAsync("/v1/admin/identity/_test/protected");
        beforeChange.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var permissionId = await db.Permissions
                .Where(x => x.Code == "identity.admin.session.manage")
                .Select(x => x.Id)
                .SingleAsync();

            var roleIds = await db.AccountRoles
                .Where(x => x.AccountId == admin.AccountId)
                .Select(x => x.RoleId)
                .Distinct()
                .ToListAsync();

            var mappings = await db.RolePermissions
                .Where(x => roleIds.Contains(x.RoleId) && x.PermissionId == permissionId)
                .ToListAsync();

            db.RolePermissions.RemoveRange(mappings);

            var account = await db.Accounts.SingleAsync(x => x.Id == admin.AccountId);
            account.PermissionVersion += 1;

            await db.SaveChangesAsync();
        }

        var afterChange = await client.GetAsync("/v1/admin/identity/_test/protected");
        afterChange.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
