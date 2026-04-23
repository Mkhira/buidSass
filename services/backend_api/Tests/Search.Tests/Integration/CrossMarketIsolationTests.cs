using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Search.Tests.Infrastructure;

namespace Search.Tests.Integration;

[Collection("search-fixture")]
public sealed class CrossMarketIsolationTests(SearchTestFactory factory)
{
    [Fact]
    public async Task CrossMarket_NoLeakage()
    {
        await factory.ResetDatabaseAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var categoryId = await SearchTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "implant", "زرعات", "Implants");
            var brandId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");

            await SearchTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider,
                brandId,
                [categoryId],
                sku: "KSA-ONLY-1",
                nameAr: "زرعة سعودية",
                nameEn: "KSA Implant",
                marketCodes: ["ksa"]);

            await SearchTestSeedHelper.RebuildSearchIndexesAsync(scope.ServiceProvider);
        }

        var client = factory.CreateClient();

        var egResponse = await client.PostAsJsonAsync("/v1/customer/search/products", new
        {
            query = "implant",
            marketCode = "eg",
            locale = "en",
            page = 1,
            pageSize = 24,
            filters = new { },
            sort = "relevance",
        });

        egResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var egBody = await egResponse.Content.ReadFromJsonAsync<SearchProductsResponseDto>();
        egBody.Should().NotBeNull();
        egBody!.Hits.Should().BeEmpty("EG index must not leak KSA-only products");
    }

    private sealed record SearchProductsResponseDto(IReadOnlyList<SearchProductHitDto> Hits, object Facets, int TotalEstimate, int QueryDurationMs, int EngineLatencyMs, bool LocaleFallbackApplied);
    private sealed record SearchProductHitDto(Guid Id, string Sku, string Name, bool Restricted, string? RestrictionReasonCode);
}
