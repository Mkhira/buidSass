using BackendApi.Modules.Catalog.Primitives.Restriction;
using Catalog.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Tests.Integration;

[Collection("catalog-fixture")]
public sealed class RestrictionCacheTests(CatalogTestFactory factory)
{
    [Fact]
    public async Task RestrictionEvaluator_CachesResults_AcrossCalls()
    {
        await factory.ResetDatabaseAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<RestrictionCache>();
        cache.Clear();
        var evaluator = scope.ServiceProvider.GetRequiredService<RestrictionEvaluator>();

        var brandId = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
        var productId = await CatalogTestSeedHelper.CreateProductAsync(
            scope.ServiceProvider,
            brandId,
            restricted: true,
            restrictionReasonCode: "professional_verification",
            restrictionMarkets: new[] { "ksa" });

        await evaluator.CheckAsync(productId, "ksa", "unverified", CancellationToken.None);
        var cached = cache.TryGet(productId, "ksa", "unverified", out var decision);

        cached.Should().BeTrue();
        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("catalog.restricted.professional_verification");
    }

    [Fact]
    public async Task RestrictionCache_Invalidation_EvictsProductEntries()
    {
        await factory.ResetDatabaseAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<RestrictionCache>();
        cache.Clear();
        var evaluator = scope.ServiceProvider.GetRequiredService<RestrictionEvaluator>();

        var brandId = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
        var productId = await CatalogTestSeedHelper.CreateProductAsync(
            scope.ServiceProvider,
            brandId,
            restricted: true,
            restrictionReasonCode: "professional_verification",
            restrictionMarkets: new[] { "ksa" });

        await evaluator.CheckAsync(productId, "ksa", "unverified", CancellationToken.None);
        cache.TryGet(productId, "ksa", "unverified", out _).Should().BeTrue();

        cache.InvalidateProduct(productId);
        cache.TryGet(productId, "ksa", "unverified", out _).Should().BeFalse();
    }
}
