using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Identity.Authorization;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Integration;

public sealed class RoleScopeTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task RoleScope_VendorReserved_AcceptsEnumButNotSeeded()
    {
        await factory.ResetDatabaseAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        db.Roles.Add(new Role
        {
            Id = Guid.NewGuid(),
            Code = "vendor.temp",
            NameAr = "vendor.temp",
            NameEn = "vendor.temp",
            Scope = "vendor",
            System = false,
        });
        await db.SaveChangesAsync();

        var vendorRole = await db.Roles.SingleOrDefaultAsync(x => x.Code == "vendor.temp");
        vendorRole.Should().NotBeNull();
        vendorRole!.Scope.Should().Be("vendor");

        var seededVendorRoles = await db.Roles.CountAsync(x => x.Scope == "vendor" && x.System);
        seededVendorRoles.Should().Be(0);
    }

    [Fact]
    public async Task CustomerCompanyOwner_RolePermissionResolvesForB2B()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var customer = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "company-owner@local.dev",
            password: "StrongPassword!123");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var ownerRole = await db.Roles.SingleOrDefaultAsync(x => x.Code == "customer.company_owner");
            if (ownerRole is null)
            {
                ownerRole = new Role
                {
                    Id = Guid.NewGuid(),
                    Code = "customer.company_owner",
                    NameAr = "customer.company_owner",
                    NameEn = "customer.company_owner",
                    Scope = "platform",
                    System = true,
                };
                db.Roles.Add(ownerRole);
            }

            var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == "customer.company.manage");
            if (permission is null)
            {
                permission = new Permission
                {
                    Id = Guid.NewGuid(),
                    Code = "customer.company.manage",
                    Description = "customer.company.manage",
                };
                db.Permissions.Add(permission);
            }

            var mappingExists = await db.RolePermissions.AnyAsync(x => x.RoleId == ownerRole.Id && x.PermissionId == permission.Id);
            if (!mappingExists)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = ownerRole.Id,
                    PermissionId = permission.Id,
                });
            }

            db.AccountRoles.Add(new AccountRole
            {
                AccountId = customer.AccountId,
                RoleId = ownerRole.Id,
                MarketCode = "ksa",
                GrantedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        }

        var signIn = await client.PostAsJsonAsync(
            "/v1/customer/identity/sign-in",
            new RoleScopeCustomerSignInRequest(customer.Email, customer.Password));
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await signIn.Content.ReadFromJsonAsync<AuthSessionResponse>();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body!.AccessToken);
        var principal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(jwt.Claims, authenticationType: "Test"));

        await using var evaluationScope = factory.Services.CreateAsyncScope();
        var policyEvaluator = evaluationScope.ServiceProvider.GetRequiredService<PolicyEvaluator>();
        var decision = await policyEvaluator.EvaluateAsync(
            principal,
            "customer.company.manage",
            cancellationToken: CancellationToken.None);

        decision.IsAllowed.Should().BeTrue();
    }
}

public sealed record RoleScopeCustomerSignInRequest(string Identifier, string Password);
