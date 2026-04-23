using BackendApi.Features.Seeding;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Identity.Seeding;

public sealed class IdentityDevDataSeeder : ISeeder
{
    public string Name => "identity.dev-data";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => ["identity.reference-data"];

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        if (!ctx.Env.IsDevelopment())
        {
            return;
        }

        var db = ctx.Services.GetRequiredService<IdentityDbContext>();
        var hasher = ctx.Services.GetRequiredService<Argon2idHasher>();

        var superAdmin = await EnsureAccountAsync(
            db,
            hasher,
            surface: "admin",
            marketCode: "platform",
            email: "super-admin@local.dev",
            locale: "en",
            ct);

        await AssignRoleAsync(db, superAdmin.Id, "platform.super_admin", "platform", ct);

        var customerKsa = await EnsureAccountAsync(
            db,
            hasher,
            surface: "customer",
            marketCode: "ksa",
            email: "customer-ksa@local.dev",
            locale: "ar",
            ct);

        await AssignRoleAsync(db, customerKsa.Id, "customer.standard", "ksa", ct);

        var customerEg = await EnsureAccountAsync(
            db,
            hasher,
            surface: "customer",
            marketCode: "eg",
            email: "customer-eg@local.dev",
            locale: "ar",
            ct);

        await AssignRoleAsync(db, customerEg.Id, "customer.company_owner", "eg", ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task<Account> EnsureAccountAsync(
        IdentityDbContext db,
        Argon2idHasher hasher,
        string surface,
        string marketCode,
        string email,
        string locale,
        CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var existing = await db.Accounts.FirstOrDefaultAsync(
            x => x.Surface == surface && x.EmailNormalized == normalized,
            ct);

        if (existing is not null)
        {
            return existing;
        }

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Surface = surface,
            MarketCode = marketCode,
            EmailNormalized = normalized,
            EmailDisplay = email,
            PasswordHash = hasher.HashPassword("DevOnly!12345", surface == "admin" ? SurfaceKind.Admin : SurfaceKind.Customer),
            PasswordHashVersion = 1,
            Status = "active",
            EmailVerifiedAt = DateTimeOffset.UtcNow,
            Locale = locale,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.Accounts.Add(account);
        return account;
    }

    private static async Task AssignRoleAsync(
        IdentityDbContext db,
        Guid accountId,
        string roleCode,
        string marketCode,
        CancellationToken ct)
    {
        var roleId = await db.Roles.Where(x => x.Code == roleCode).Select(x => x.Id).SingleAsync(ct);
        var exists = await db.AccountRoles.AnyAsync(
            x => x.AccountId == accountId && x.RoleId == roleId && x.MarketCode == marketCode,
            ct);

        if (exists)
        {
            return;
        }

        db.AccountRoles.Add(new AccountRole
        {
            AccountId = accountId,
            RoleId = roleId,
            MarketCode = marketCode,
            GrantedAt = DateTimeOffset.UtcNow,
        });
    }
}
