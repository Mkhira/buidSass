using BackendApi.Features.Seeding;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Identity.Seeding;

public sealed class IdentityReferenceDataSeeder : ISeeder
{
    public string Name => "identity.reference-data";
    public int Version => 2;
    public IReadOnlyList<string> DependsOn => [];

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Services.GetRequiredService<IdentityDbContext>();

        var roles = new[]
        {
            new Role { Id = Guid.NewGuid(), Code = "platform.super_admin", NameAr = "مشرف المنصة", NameEn = "Platform Super Admin", Scope = "platform", System = true },
            new Role { Id = Guid.NewGuid(), Code = "platform.finance", NameAr = "فريق المالية", NameEn = "Platform Finance", Scope = "platform", System = true },
            new Role { Id = Guid.NewGuid(), Code = "platform.support", NameAr = "فريق الدعم", NameEn = "Platform Support", Scope = "platform", System = true },
            new Role { Id = Guid.NewGuid(), Code = "catalog.editor", NameAr = "محرر الكتالوج", NameEn = "Catalog Editor", Scope = "platform", System = true },
            new Role { Id = Guid.NewGuid(), Code = "catalog.publisher", NameAr = "ناشر الكتالوج", NameEn = "Catalog Publisher", Scope = "platform", System = true },
            new Role { Id = Guid.NewGuid(), Code = "pricing.editor", NameAr = "محرر التسعير", NameEn = "Pricing Editor", Scope = "platform", System = true },
            new Role { Id = Guid.NewGuid(), Code = "pricing.admin", NameAr = "مشرف التسعير", NameEn = "Pricing Admin", Scope = "platform", System = true },
            new Role { Id = Guid.NewGuid(), Code = "inventory.operator", NameAr = "مشغل المخزون", NameEn = "Inventory Operator", Scope = "platform", System = true },
            new Role { Id = Guid.NewGuid(), Code = "customer.standard", NameAr = "عميل", NameEn = "Customer", Scope = "market", System = true },
            new Role { Id = Guid.NewGuid(), Code = "customer.company_owner", NameAr = "مالك الشركة", NameEn = "Company Owner", Scope = "market", System = true },
        };

        var permissions = new[]
        {
            "identity.admin.invite",
            "identity.admin.revoke",
            "identity.admin.invitation.revoke",
            "identity.admin.role.change",
            "identity.admin.session.manage",
            "identity.admin.session.revoke",
            "identity.admin.mfa.reset",
            "identity.customer.session.manage",
            "identity.customer.profile.manage",
            "identity.customer.self",
            // Catalog (spec 005)
            "catalog.category.write",
            "catalog.brand.write",
            "catalog.manufacturer.write",
            "catalog.product.write",
            "catalog.product.submit",
            "catalog.product.publish",
            "catalog.product.archive",
            "catalog.media.write",
            "catalog.document.write",
            "catalog.bulk_import",
            // Search (spec 006)
            "search.reindex",
            "search.index.manage",
            "search.read",
            // Pricing (spec 007-a)
            "pricing.tax.read",
            "pricing.tax.write",
            "pricing.promotion.read",
            "pricing.promotion.write",
            "pricing.coupon.read",
            "pricing.coupon.write",
            "pricing.tier.read",
            "pricing.tier.write",
            "pricing.explanation.read",
            "pricing.internal.calculate",
            // Inventory (spec 008)
            "inventory.stock.read",
            "inventory.batch.read",
            "inventory.batch.write",
            "inventory.movement.read",
            "inventory.movement.write",
            "inventory.reservation.read",
            "inventory.alert.read",
            "inventory.internal.reserve",
            "inventory.internal.release",
            "inventory.internal.convert",
            "inventory.internal.return",
        }.Select(code => new Permission
        {
            Id = Guid.NewGuid(),
            Code = code,
            Description = code,
        }).ToArray();

        foreach (var role in roles)
        {
            if (!await db.Roles.AnyAsync(x => x.Code == role.Code, ct))
            {
                db.Roles.Add(role);
            }
        }

        foreach (var permission in permissions)
        {
            if (!await db.Permissions.AnyAsync(x => x.Code == permission.Code, ct))
            {
                db.Permissions.Add(permission);
            }
        }

        await db.SaveChangesAsync(ct);

        var roleCodes = await db.Roles.ToDictionaryAsync(x => x.Code, x => x.Id, ct);
        var permissionCodes = await db.Permissions.ToDictionaryAsync(x => x.Code, x => x.Id, ct);

        var matrix = new Dictionary<string, string[]>
        {
            ["platform.super_admin"] = permissionCodes.Keys.ToArray(),
            ["platform.finance"] =
            [
                "identity.admin.session.manage",
                "identity.admin.role.change",
            ],
            ["platform.support"] =
            [
                "identity.admin.session.manage",
                "identity.customer.profile.manage",
                "search.index.manage",
                "search.read",
            ],
            ["customer.standard"] =
            [
                "identity.customer.profile.manage",
                "identity.customer.session.manage",
                "identity.customer.self",
            ],
            ["customer.company_owner"] =
            [
                "identity.customer.profile.manage",
                "identity.customer.session.manage",
                "identity.customer.self",
            ],
            ["catalog.editor"] =
            [
                "catalog.category.write",
                "catalog.brand.write",
                "catalog.manufacturer.write",
                "catalog.product.write",
                "catalog.product.submit",
                "catalog.media.write",
                "catalog.document.write",
                "catalog.bulk_import",
            ],
            ["catalog.publisher"] =
            [
                "catalog.product.publish",
                "catalog.product.archive",
            ],
            ["pricing.editor"] =
            [
                "pricing.promotion.read",
                "pricing.promotion.write",
                "pricing.coupon.read",
                "pricing.coupon.write",
                "pricing.tier.read",
                "pricing.tier.write",
                "pricing.explanation.read",
            ],
            ["pricing.admin"] =
            [
                "pricing.tax.read",
                "pricing.tax.write",
                "pricing.promotion.read",
                "pricing.promotion.write",
                "pricing.coupon.read",
                "pricing.coupon.write",
                "pricing.tier.read",
                "pricing.tier.write",
                "pricing.explanation.read",
                "pricing.internal.calculate",
            ],
            ["inventory.operator"] =
            [
                "inventory.stock.read",
                "inventory.batch.read",
                "inventory.batch.write",
                "inventory.movement.read",
                "inventory.movement.write",
                "inventory.reservation.read",
                "inventory.alert.read",
            ],
        };

        foreach (var (roleCode, permissionSet) in matrix)
        {
            var roleId = roleCodes[roleCode];
            foreach (var permissionCode in permissionSet)
            {
                var permissionId = permissionCodes[permissionCode];
                if (!await db.RolePermissions.AnyAsync(
                        x => x.RoleId == roleId && x.PermissionId == permissionId, ct))
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
