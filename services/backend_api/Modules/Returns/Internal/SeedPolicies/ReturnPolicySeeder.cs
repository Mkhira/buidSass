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
        // CR Major fix — single read + AddRange is race-tolerant under concurrent boots.
        // Two parallel callers may both compute the same `toInsert`, but the second
        // SaveChanges fails on the PK conflict, which we swallow so startup stays idempotent.
        var existingMarkets = await db.ReturnPolicies
            .AsNoTracking()
            .Select(x => x.MarketCode)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existingMarkets, StringComparer.OrdinalIgnoreCase);
        var toInsert = defaults.Where(p => !existingSet.Contains(p.MarketCode)).ToArray();
        if (toInsert.Length == 0) return 0;

        db.ReturnPolicies.AddRange(toInsert);
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent peer inserted first — the migration's ON CONFLICT seed and this
            // seeder are belt-and-braces; the policy rows exist, that is what matters.
            return 0;
        }
    }
}
