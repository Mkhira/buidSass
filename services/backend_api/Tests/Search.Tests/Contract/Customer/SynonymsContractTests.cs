using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Search.Tests.Infrastructure;

namespace Search.Tests.Contract.Customer;

[Collection("search-fixture")]
public sealed class SynonymsContractTests(SearchTestFactory factory)
{
    [Fact]
    public async Task Synonyms_LoadedAtBoot_ArDental_Expands()
    {
        await factory.ResetDatabaseAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var categoryId = await SearchTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "prosthesis", "تعويضات", "Prosthesis");
            var brandId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme", "اكمي", "Acme");

            await SearchTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider,
                brandId,
                [categoryId],
                sku: "SYN-AR-001",
                nameAr: "طقم اسنان متكامل",
                nameEn: "Full Denture Set",
                marketCodes: ["ksa"]);

            await SearchTestSeedHelper.RebuildSearchIndexesAsync(scope.ServiceProvider);
        }

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/customer/search/products", new
        {
            query = "طقم صناعي",
            marketCode = "ksa",
            locale = "ar",
            page = 1,
            pageSize = 24,
            filters = new { },
            sort = "relevance",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SearchProductsResponseDto>();
        body.Should().NotBeNull();
        body!.Hits.Should().ContainSingle(h => h.Sku == "SYN-AR-001");
    }

    private sealed record SearchProductsResponseDto(IReadOnlyList<SearchProductHitDto> Hits, object Facets, int TotalEstimate, int QueryDurationMs, int EngineLatencyMs, bool LocaleFallbackApplied);
    private sealed record SearchProductHitDto(Guid Id, string Sku, string? Barcode, string Name, bool Restricted, string? RestrictionReasonCode);
}
