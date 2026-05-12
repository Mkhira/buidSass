using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Seeding;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Seeding;

/// <summary>
/// Spec 007-b T117 — when the host environment is Production, the dev seeder
/// must short-circuit (no rows written). The Constitution forbids
/// dev-flavoured synthetic data ever reaching Production, regardless of
/// configuration flags.
/// </summary>
[Collection("pricing-fixture")]
public sealed class PromotionsV1SeederGuardTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Seeder_InProductionEnvironment_IsNoOp()
    {
        await factory.ResetDatabaseAsync();
        await RunSeederAsync("Production");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var totalRows =
            await db.Coupons.CountAsync() +
            await db.Promotions.CountAsync() +
            await db.Campaigns.CountAsync();
        totalRows.Should().Be(0,
            "the dev seeder must NEVER write rows when EnvironmentName=Production");
    }

    [Theory]
    [InlineData("Test")]
    [InlineData("CustomEnvironment")]
    public async Task Seeder_InNonDevNonStagingEnvironment_IsNoOp(string env)
    {
        await factory.ResetDatabaseAsync();
        await RunSeederAsync(env);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var rows = await db.Coupons.CountAsync();
        rows.Should().Be(0,
            $"the dev seeder must short-circuit for environment '{env}'");
    }

    private async Task RunSeederAsync(string environmentName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var seeder = new PromotionsV1DevSeeder();
        var ctx = new SeedContext(
            Db: scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            Services: scope.ServiceProvider,
            Size: DatasetSize.Small,
            Env: new SeederTestEnvironment(environmentName),
            Logger: NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }
}
