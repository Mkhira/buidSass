using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Subscribers;

/// <summary>
/// Spec 007-b T129 — subscribes to <see cref="CatalogSkuArchivedEvent"/> (spec 005)
/// and marks every referencing Promotion / BusinessPricingRow with the
/// "applies-to-broken" flag (FR-034a / data-model §7 / research §R3, R15).
///
/// Coupons are NOT touched by this handler — coupons are code-keyed and have
/// no SKU-list dependency.
///
/// Idempotent: each row's <c>*_broken_at_utc</c> column is the no-op marker.
/// </summary>
public sealed class CatalogSkuArchivedHandler : ICatalogSkuArchivedSubscriber
{
    private readonly PricingDbContext _db;
    private readonly TimeProvider _time;

    public CatalogSkuArchivedHandler(PricingDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task HandleAsync(CatalogSkuArchivedEvent e, CancellationToken ct)
    {
        var nowUtc = _time.GetUtcNow();

        // Promotions reference SKUs via AppliesToProductIds (Guid[]).
        var promos = await _db.Promotions
            .Where(p => p.AppliesToProductIds != null &&
                        p.AppliesToProductIds.Contains(e.SkuId) &&
                        !p.AppliesToBroken &&
                        p.State != LifecycleState.Expired)
            .ToListAsync(ct);
        foreach (var p in promos)
        {
            p.AppliesToBroken = true;
            p.AppliesToBrokenAtUtc = nowUtc;
            p.UpdatedAt = nowUtc;
        }

        // ProductTierPrice rows match on the archived SKU's product_id. We
        // re-use the `CompanyLinkBroken` flag because the entity has no
        // separate "product archived" column at V1 (data-model §2.3 lists
        // CompanyLinkBroken as the single broken-reference signal). The audit
        // trail differentiates cause via the deactivation event payload.
        var tierRows = await _db.ProductTierPrices
            .Where(r => r.ProductId == e.SkuId &&
                        !r.CompanyLinkBroken &&
                        r.State == BusinessPricingState.Active)
            .ToListAsync(ct);
        foreach (var r in tierRows)
        {
            r.CompanyLinkBroken = true;
            r.CompanyLinkBrokenAtUtc = nowUtc;
            r.UpdatedAt = nowUtc;
        }

        if (promos.Count == 0 && tierRows.Count == 0)
        {
            return; // idempotent no-op
        }

        await _db.SaveChangesAsync(ct);
    }
}
