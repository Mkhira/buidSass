using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
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
/// Spec 007-b T116 — <see cref="PromotionsV1DevSeeder"/> populates ≥ 1 row in
/// each lifecycle state for Coupons + Promotions, plus the 3 campaigns
/// promised by US6 (research §R8).
/// </summary>
[Collection("pricing-fixture")]
public sealed class PromotionsV1SeederStateCoverageTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Seeder_Populates_AllLifecycleStates_ForCoupons()
    {
        await factory.ResetDatabaseAsync();
        await RunSeederAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var couponStates = await db.Coupons.AsNoTracking()
            .Select(c => c.State)
            .Distinct()
            .ToListAsync();

        couponStates.Should().Contain(LifecycleState.Draft);
        couponStates.Should().Contain(LifecycleState.Scheduled);
        couponStates.Should().Contain(LifecycleState.Active);
        couponStates.Should().Contain(LifecycleState.Deactivated);
        couponStates.Should().Contain(LifecycleState.Expired);
    }

    [Fact]
    public async Task Seeder_Populates_PromotionLifecycleStates_AndCampaigns()
    {
        await factory.ResetDatabaseAsync();
        await RunSeederAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();

        var promoStates = await db.Promotions.AsNoTracking()
            .Select(p => p.State)
            .Distinct()
            .ToListAsync();
        promoStates.Should().Contain(LifecycleState.Active);
        promoStates.Should().Contain(LifecycleState.Scheduled);
        promoStates.Should().Contain(LifecycleState.Deactivated);
        promoStates.Should().Contain(LifecycleState.Expired);

        var campaignCount = await db.Campaigns.AsNoTracking().CountAsync();
        campaignCount.Should().BeGreaterThanOrEqualTo(3,
            "US6 promises 3 campaigns in the dev seeder dataset");
    }

    private async Task RunSeederAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var seeder = new PromotionsV1DevSeeder();
        var ctx = new SeedContext(
            Db: scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            Services: scope.ServiceProvider,
            Size: DatasetSize.Small,
            Env: new SeederTestEnvironment("Development"),
            Logger: NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }
}

/// <summary>
/// Hand-rolled <see cref="IHostEnvironment"/> for seeder tests. The shared
/// fixture's runtime env is "Test"; this stub lets individual tests substitute
/// "Development" / "Staging" / "Production" to exercise the seeder's
/// environment gate without rebuilding the host.
/// </summary>
internal sealed class SeederTestEnvironment : IHostEnvironment
{
    public SeederTestEnvironment(string environmentName)
    {
        EnvironmentName = environmentName;
    }
    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; } = "Pricing.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
        = new Microsoft.Extensions.FileProviders.NullFileProvider();
}
