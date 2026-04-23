using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;

namespace Identity.Tests.Infrastructure;

public static class AdminTestDataHelper
{
    public static async Task<AdminSeedResult> SeedAdminAsync(
        IdentityTestFactory factory,
        string email,
        string password,
        string[] permissions,
        bool withTotpFactor,
        string roleCode = "platform.super_admin")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Argon2idHasher>();
        var protector = scope.ServiceProvider
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("identity.admin.totp.secret.v1");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Surface = "admin",
            MarketCode = "platform",
            EmailNormalized = normalizedEmail,
            EmailDisplay = email,
            PasswordHash = hasher.HashPassword(password, SurfaceKind.Admin),
            PasswordHashVersion = 1,
            Status = "active",
            EmailVerifiedAt = now,
            Locale = "en",
            DisplayName = "Admin Test",
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

        var effectivePermissions = permissions
            .Concat(string.Equals(roleCode, "platform.super_admin", StringComparison.OrdinalIgnoreCase)
                ? ["identity.admin.invite", "identity.admin.revoke", "identity.admin.invitation.revoke", "identity.admin.role.change", "identity.admin.session.manage", "identity.admin.session.revoke", "identity.admin.mfa.reset"]
                : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var permissionCode in effectivePermissions)
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

            var rolePermissionExists = await db.RolePermissions.AnyAsync(
                x => x.RoleId == role.Id && x.PermissionId == permission.Id);
            if (!rolePermissionExists)
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

        string? totpSecret = null;
        if (withTotpFactor)
        {
            var secret = KeyGeneration.GenerateRandomKey(20);
            totpSecret = Base32Encoding.ToString(secret);

            db.AdminMfaFactors.Add(new AdminMfaFactor
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                Kind = "totp",
                SecretEncrypted = TotpSecretCodec.Encode(protector, secret),
                ConfirmedAt = now,
                CreatedAt = now,
                RecoveryCodesHash = "[]",
            });
        }

        await db.SaveChangesAsync();

        return new AdminSeedResult(account.Id, email, password, totpSecret);
    }

    public static string GenerateTotpCode(string base32Secret)
    {
        var secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes);
        return totp.ComputeTotp(DateTime.UtcNow);
    }

    public static async Task<string> SignInAndCompleteMfaAsync(
        HttpClient client,
        string email,
        string password,
        string base32Secret)
    {
        var signIn = await client.PostAsJsonAsync(
            "/v1/admin/identity/sign-in",
            new AdminSignInRequest(email, password));
        signIn.EnsureSuccessStatusCode();

        var signInBody = await signIn.Content.ReadFromJsonAsync<AdminSignInResponse>();
        if (signInBody?.MfaChallenge is null)
        {
            if (signInBody?.AuthSession is null)
            {
                throw new InvalidOperationException("Admin sign-in returned neither an MFA challenge nor an auth session.");
            }

            return signInBody.AuthSession.AccessToken;
        }

        var code = GenerateTotpCode(base32Secret);

        var challenge = await client.PostAsJsonAsync(
            "/v1/admin/identity/mfa/challenge",
            new AdminMfaChallengeRequest(signInBody.MfaChallenge.ChallengeId, "totp", code));
        challenge.EnsureSuccessStatusCode();

        var challengeBody = await challenge.Content.ReadFromJsonAsync<AuthSessionResponse>();
        return challengeBody!.AccessToken;
    }

    public static void SetBearer(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public static byte[] HashToken(string token)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}

public sealed record AdminSeedResult(Guid AccountId, string Email, string Password, string? TotpSecret);

public sealed record AdminSignInRequest(string Email, string Password);
public sealed record AdminSignInResponse(MfaChallengeResponse? MfaChallenge, AuthSessionResponse? AuthSession);
public sealed record MfaChallengeResponse(Guid ChallengeId, string Kind);
public sealed record AdminMfaChallengeRequest(Guid ChallengeId, string Kind, string Code);
public sealed record AuthSessionResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
