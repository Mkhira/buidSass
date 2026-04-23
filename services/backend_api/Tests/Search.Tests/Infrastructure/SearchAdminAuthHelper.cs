using System.Net.Http.Headers;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Search.Tests.Infrastructure;

public static class SearchAdminAuthHelper
{
    public static async Task<string> IssueAdminTokenAsync(
        SearchTestFactory factory,
        string[] permissions,
        string roleCode = "platform.support")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Argon2idHasher>();
        var authSessions = scope.ServiceProvider.GetRequiredService<AdminAuthSessionService>();

        var now = DateTimeOffset.UtcNow;
        var email = $"search-admin-{Guid.NewGuid():N}@example.test";

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Surface = "admin",
            MarketCode = "platform",
            EmailNormalized = email.ToLowerInvariant(),
            EmailDisplay = email,
            PasswordHash = hasher.HashPassword("SearchTests!123", SurfaceKind.Admin),
            PasswordHashVersion = 1,
            PermissionVersion = 1,
            Status = "active",
            EmailVerifiedAt = now,
            Locale = "en",
            DisplayName = "Search Admin",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Accounts.Add(account);

        var role = await db.Roles.SingleOrDefaultAsync(x => x.Code == roleCode);
        if (role is null)
        {
            role = new Role
            {
                Id = Guid.NewGuid(),
                Code = roleCode,
                NameAr = roleCode,
                NameEn = roleCode,
                Scope = "platform",
                System = true,
            };
            db.Roles.Add(role);
        }

        foreach (var permissionCode in permissions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == permissionCode);
            if (permission is null)
            {
                permission = new Permission
                {
                    Id = Guid.NewGuid(),
                    Code = permissionCode,
                    Description = permissionCode,
                };
                db.Permissions.Add(permission);
            }

            var linkExists = await db.RolePermissions.AnyAsync(x => x.RoleId == role.Id && x.PermissionId == permission.Id);
            if (!linkExists)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permission.Id,
                });
            }
        }

        db.AccountRoles.Add(new AccountRole
        {
            AccountId = account.Id,
            RoleId = role.Id,
            MarketCode = "platform",
            GrantedAt = now,
        });

        await db.SaveChangesAsync();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.UserAgent = "search-tests";

        var session = await authSessions.IssueAdminSessionAsync(account, httpContext, CancellationToken.None);
        return session.AccessToken;
    }

    public static void SetBearer(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }
}
