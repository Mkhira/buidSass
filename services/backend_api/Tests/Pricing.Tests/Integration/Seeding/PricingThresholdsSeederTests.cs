using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Seeding;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Seeding;

/// <summary>
/// Spec 007-b T049 — confirms <see cref="PricingThresholdsSeeder"/> produces
/// exactly 2 rows (SA + EG), preserves them across re-runs (idempotency), and
/// matches the per-market values from research §R8.
///
/// The 007-b foundation migration also seeds these rows via raw SQL on first
/// install. This test validates that the seeder is a no-op against an already-
/// seeded DB AND that the seeded values are correct end-to-end.
/// </summary>
[Collection("pricing-fixture")]
public sealed class PricingThresholdsSeederTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Seeder_Produces_Both_Markets_With_Gate_On()
    {
        await ResetThresholdsAsync();
        await RunSeederAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();

        var rows = await db.CommercialThresholds.AsNoTracking()
            .OrderBy(r => r.MarketCode)
            .ToListAsync();

        rows.Should().HaveCount(2, "exactly SA + EG markets are seeded (research §R8)");

        var eg = rows.Single(r => r.MarketCode == "EG");
        eg.GateEnabled.Should().BeTrue();
        eg.ThresholdPercentOff.Should().Be(30.00m);
        eg.ThresholdAmountOffMinor.Should().Be(25_000_000L);
        eg.ThresholdDurationDays.Should().Be(14);
        eg.CouponInFlightGraceSeconds.Should().Be(1800);
        eg.PromotionInFlightGraceSeconds.Should().Be(1800);

        var sa = rows.Single(r => r.MarketCode == "SA");
        sa.GateEnabled.Should().BeTrue();
        sa.ThresholdPercentOff.Should().Be(30.00m);
        sa.ThresholdAmountOffMinor.Should().Be(5_000_000L);
        sa.ThresholdDurationDays.Should().Be(14);
        sa.CouponInFlightGraceSeconds.Should().Be(1800);
        sa.PromotionInFlightGraceSeconds.Should().Be(1800);
    }

    [Fact]
    public async Task Seeder_Is_Idempotent_Across_Multiple_Runs()
    {
        await ResetThresholdsAsync();
        await RunSeederAsync();
        await RunSeederAsync();
        await RunSeederAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();

        var count = await db.CommercialThresholds.CountAsync();
        count.Should().Be(2, "running the seeder repeatedly must not duplicate rows");
    }

    [Fact]
    public async Task Seeder_Does_Not_Overwrite_Tuned_Values()
    {
        // Operators may have tuned thresholds via UpdateThresholds — the seeder
        // must NOT clobber those values on subsequent deploys.
        await ResetThresholdsAsync();
        await RunSeederAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            var sa = await db.CommercialThresholds.SingleAsync(r => r.MarketCode == "SA");
            sa.ThresholdPercentOff = 99m;
            sa.GateEnabled = false;
            await db.SaveChangesAsync();
        }

        await RunSeederAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            var sa = await db.CommercialThresholds.AsNoTracking()
                .SingleAsync(r => r.MarketCode == "SA");
            sa.ThresholdPercentOff.Should().Be(99m, "tuned values must survive re-seed");
            sa.GateEnabled.Should().BeFalse("tuned values must survive re-seed");
        }
    }

    private async Task ResetThresholdsAsync()
    {
        // Tests in this fixture share state across the run; clearing the
        // commercial_thresholds table at the top of each test guarantees the
        // seeder under test starts from an empty slate, regardless of order.
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE TABLE pricing.commercial_thresholds RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task RunSeederAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var seeder = new PricingThresholdsSeeder();
        var ctx = new SeedContext(
            Db: scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            Services: scope.ServiceProvider,
            Size: DatasetSize.Small,
            Env: scope.ServiceProvider.GetRequiredService<IHostEnvironment>(),
            Logger: NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }
}
