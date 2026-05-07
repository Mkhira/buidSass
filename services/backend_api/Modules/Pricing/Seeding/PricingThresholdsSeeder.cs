using BackendApi.Features.Seeding;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Pricing.Seeding;

/// <summary>
/// Spec 007-b T040 / research §R8.
///
/// Idempotently upserts the per-market <see cref="CommercialThreshold"/> rows that
/// drive <c>HighImpactGate</c>. Runs in every environment (Dev / Staging / Production)
/// because the gate must be ON at launch with conservative defaults per market
/// (Clarification §Q1). Existing rows are NEVER overwritten — operators may have
/// tuned thresholds via the <c>UpdateThresholds</c> admin endpoint, and tuning
/// must survive subsequent deploys.
///
/// Seeded values (research §R8 §135–138):
///
/// | Market | gate_enabled | percent_off | amount_off_minor | duration_days | grace_seconds |
/// |--------|--------------|-------------|------------------|---------------|---------------|
/// | SA     | true         | 30          | 5_000_000        | 14            | 1800          |
/// | EG     | true         | 30          | 25_000_000       | 14            | 1800          |
///
/// The 007-b foundation migration also seeds these rows on first install via
/// <c>ON CONFLICT (MarketCode) DO NOTHING</c>; this seeder backstops the migration
/// for hand-bootstrapped DBs and rebuilds, and keeps the seed contract uniform
/// with peer modules (Identity, Catalog, Pricing reference data).
/// </summary>
public sealed class PricingThresholdsSeeder : ISeeder
{
    public string Name => "pricing.commercial-thresholds";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => [];

    private static readonly IReadOnlyList<CommercialThreshold> SeedRows =
    [
        new CommercialThreshold
        {
            MarketCode = "SA",
            GateEnabled = true,
            ThresholdPercentOff = 30.00m,
            ThresholdAmountOffMinor = 5_000_000L,
            ThresholdDurationDays = 14,
            CouponInFlightGraceSeconds = 1800,
            PromotionInFlightGraceSeconds = 1800,
        },
        new CommercialThreshold
        {
            MarketCode = "EG",
            GateEnabled = true,
            ThresholdPercentOff = 30.00m,
            ThresholdAmountOffMinor = 25_000_000L,
            ThresholdDurationDays = 14,
            CouponInFlightGraceSeconds = 1800,
            PromotionInFlightGraceSeconds = 1800,
        },
    ];

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Services.GetRequiredService<PricingDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;

        foreach (var row in SeedRows)
        {
            var exists = await db.CommercialThresholds
                .AsNoTracking()
                .AnyAsync(t => t.MarketCode == row.MarketCode, ct);
            if (exists)
            {
                continue;
            }

            db.CommercialThresholds.Add(new CommercialThreshold
            {
                MarketCode = row.MarketCode,
                GateEnabled = row.GateEnabled,
                ThresholdPercentOff = row.ThresholdPercentOff,
                ThresholdAmountOffMinor = row.ThresholdAmountOffMinor,
                ThresholdDurationDays = row.ThresholdDurationDays,
                CouponInFlightGraceSeconds = row.CouponInFlightGraceSeconds,
                PromotionInFlightGraceSeconds = row.PromotionInFlightGraceSeconds,
                UpdatedAtUtc = nowUtc,
                UpdatedByActorId = CommercialPermissions.SystemActorId,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
