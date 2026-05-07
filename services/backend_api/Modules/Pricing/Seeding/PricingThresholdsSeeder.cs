using BackendApi.Features.Seeding;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

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

        // Atomic upsert via ON CONFLICT DO NOTHING — guarantees idempotency under
        // concurrent multi-instance startup (CodeRabbit Major: check-then-insert
        // race). Mirrors the foundation migration's seed-INSERT pattern. The
        // parameter list is bound positionally; column names use the project's
        // PascalCase EF mapping (citext market_code primary key, quoted).
        const string Sql = @"
            INSERT INTO pricing.commercial_thresholds
                (""MarketCode"", ""GateEnabled"",
                 ""ThresholdPercentOff"", ""ThresholdAmountOffMinor"", ""ThresholdDurationDays"",
                 ""CouponInFlightGraceSeconds"", ""PromotionInFlightGraceSeconds"",
                 ""UpdatedAtUtc"", ""UpdatedByActorId"")
            VALUES (@p_market, @p_gate,
                    @p_pct, @p_amount, @p_days,
                    @p_coupon_grace, @p_promo_grace,
                    @p_updated_at, @p_updated_by)
            ON CONFLICT (""MarketCode"") DO NOTHING;";

        foreach (var row in SeedRows)
        {
            var parameters = new object[]
            {
                // Use Text rather than Citext: PostgreSQL implicitly casts text → citext
                // on INSERT, and the ON CONFLICT lookup is resolved via OID-based table
                // schema (not client-side type matching). This avoids an OID-cache-miss
                // failure ("The NpgsqlDbType 'Citext' isn't present in your database")
                // on hand-bootstrapped fresh DBs where the connection pool primed before
                // the citext extension was created (CodeRabbit Minor, loop 2).
                new NpgsqlParameter("p_market", NpgsqlDbType.Text) { Value = row.MarketCode },
                new NpgsqlParameter("p_gate", NpgsqlDbType.Boolean) { Value = row.GateEnabled },
                new NpgsqlParameter("p_pct", NpgsqlDbType.Numeric)
                {
                    Value = (object?)row.ThresholdPercentOff ?? DBNull.Value,
                },
                new NpgsqlParameter("p_amount", NpgsqlDbType.Bigint)
                {
                    Value = (object?)row.ThresholdAmountOffMinor ?? DBNull.Value,
                },
                new NpgsqlParameter("p_days", NpgsqlDbType.Integer)
                {
                    Value = (object?)row.ThresholdDurationDays ?? DBNull.Value,
                },
                new NpgsqlParameter("p_coupon_grace", NpgsqlDbType.Integer) { Value = row.CouponInFlightGraceSeconds },
                new NpgsqlParameter("p_promo_grace", NpgsqlDbType.Integer) { Value = row.PromotionInFlightGraceSeconds },
                new NpgsqlParameter("p_updated_at", NpgsqlDbType.TimestampTz) { Value = nowUtc },
                new NpgsqlParameter("p_updated_by", NpgsqlDbType.Uuid) { Value = CommercialPermissions.SystemActorId },
            };

            await db.Database.ExecuteSqlRawAsync(Sql, parameters, ct);
        }
    }
}
