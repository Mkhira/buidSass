using BackendApi.Modules.Returns.Entities;
using BackendApi.Modules.Returns.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Internal.SeedPolicies;

/// <summary>
/// Phase B B3 — seeds <c>return_policies</c> for KSA (14 days) and EG (7 days). Idempotent:
/// existing rows are left untouched so admin edits via <c>PUT /v1/admin/return-policies</c>
/// survive subsequent boots / migrations.
/// </summary>
public sealed class ReturnPolicySeeder(ReturnsDbContext db)
{
    public async Task<int> SeedAsync(CancellationToken ct)
    {
        int inserted = 0;
        var defaults = new[]
        {
            new ReturnPolicy
            {
                MarketCode = "KSA",
                ReturnWindowDays = 14,
                AutoApproveUnderDays = null,
                RestockingFeeBp = 0,
                ShippingRefundOnFullOnly = true,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            new ReturnPolicy
            {
                MarketCode = "EG",
                ReturnWindowDays = 7,
                AutoApproveUnderDays = null,
                RestockingFeeBp = 0,
                ShippingRefundOnFullOnly = true,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
        };
        foreach (var p in defaults)
        {
            var exists = await db.ReturnPolicies.AnyAsync(x => x.MarketCode == p.MarketCode, ct);
            if (!exists)
            {
                db.ReturnPolicies.Add(p);
                inserted++;
            }
        }
        if (inserted > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return inserted;
    }
}
