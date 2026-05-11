using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Subscribers;

/// <summary>
/// Spec 007-b T130 — subscribes to <see cref="B2BCompanySuspendedEvent"/>
/// (spec 021) and marks every referencing company-override row with
/// <c>CompanyLinkBroken=true</c> (data-model §3.2 / research §R3).
///
/// Idempotent: only flips rows where the flag is currently false. The 7-day
/// grace window before auto-deactivation is enforced by
/// <see cref="Workers.BrokenReferenceAutoDeactivationWorker"/>; this handler
/// only sets the flag.
/// </summary>
public sealed class B2BCompanySuspendedHandler : IB2BCompanySuspendedSubscriber
{
    private readonly PricingDbContext _db;
    private readonly TimeProvider _time;

    public B2BCompanySuspendedHandler(PricingDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task HandleAsync(B2BCompanySuspendedEvent e, CancellationToken ct)
    {
        var nowUtc = _time.GetUtcNow();

        var rows = await _db.ProductTierPrices
            .Where(r => r.CompanyId == e.CompanyId &&
                        !r.CompanyLinkBroken &&
                        r.State == BusinessPricingState.Active)
            .ToListAsync(ct);
        if (rows.Count == 0) return;

        foreach (var r in rows)
        {
            r.CompanyLinkBroken = true;
            r.CompanyLinkBrokenAtUtc = nowUtc;
            r.UpdatedAt = nowUtc;
        }
        await _db.SaveChangesAsync(ct);
    }
}
