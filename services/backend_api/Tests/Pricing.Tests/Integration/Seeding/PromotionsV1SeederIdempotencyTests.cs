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
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Seeding;

/// <summary>
/// Spec 007-b T115 — running <see cref="PromotionsV1DevSeeder"/> twice
/// produces the same row counts; re-runs are no-ops (US6 acceptance
/// criterion).
/// </summary>
[Collection("pricing-fixture")]
public sealed class PromotionsV1SeederIdempotencyTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Seeder_IsIdempotent_AcrossMultipleRuns()
    {
        await factory.ResetDatabaseAsync();
        await RunSeederAsync();
        var firstCounts = await CaptureCountsAsync();

        await RunSeederAsync();
        await RunSeederAsync();
        var thirdCounts = await CaptureCountsAsync();

        thirdCounts.Coupons.Should().Be(firstCounts.Coupons,
            "re-running the seeder must not duplicate coupon rows");
        thirdCounts.Promotions.Should().Be(firstCounts.Promotions,
            "re-running the seeder must not duplicate promotion rows");
        thirdCounts.Campaigns.Should().Be(firstCounts.Campaigns,
            "re-running the seeder must not duplicate campaign rows");
    }

    private async Task RunSeederAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var seeder = new PromotionsV1DevSeeder();
        // Test fixture runs the host under EnvironmentName="Test" so the
        // seeder's IsDevelopment / IsStaging gate short-circuits in real
        // resolution; substitute a Development environment so the seeder
        // body executes for this assertion suite.
        var ctx = new SeedContext(
            Db: scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            Services: scope.ServiceProvider,
            Size: DatasetSize.Small,
            Env: new SeederTestEnvironment("Development"),
            Logger: NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private async Task<(int Coupons, int Promotions, int Campaigns)> CaptureCountsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        return (
            await db.Coupons.CountAsync(),
            await db.Promotions.CountAsync(),
            await db.Campaigns.CountAsync());
    }
}
