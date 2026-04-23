using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Search.Tests.Infrastructure;

namespace Search.Tests.Contract.Customer;

[Collection("search-fixture")]
public sealed class SearchProductsContractTests(SearchTestFactory factory)
{
    [Fact]
    public async Task SearchProducts_ArabicQuery_FoldsDiacritics()
    {
        await factory.ResetDatabaseAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var categoryId = await SearchTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "gloves", "قفازات", "Gloves");
            var brandId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme", "اكمي", "Acme");
            await SearchTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider,
                brandId,
                [categoryId],
                sku: "DX-001-KSA",
                nameAr: "قُفَّازَات جِرَاحِيَّة",
                nameEn: "Surgical Gloves",
                marketCodes: ["ksa"]);
            await SearchTestSeedHelper.RebuildSearchIndexesAsync(scope.ServiceProvider);
        }

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/customer/search/products", new
        {
            query = "قفازات جراحية",
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
        body!.Hits.Should().ContainSingle(h => h.Sku == "DX-001-KSA");
    }

    [Fact]
    public async Task SearchProducts_FacetFilter_NarrowsResults()
    {
        await factory.ResetDatabaseAsync();

        string brandA;
        string brandB;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var categoryId = await SearchTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "gloves", "قفازات", "Gloves");
            var brandAId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme", "اكمي", "Acme");
            var brandBId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "dent", "دنت", "Dent");

            brandA = brandAId.ToString();
            brandB = brandBId.ToString();

            await SearchTestSeedHelper.CreatePublishedProductAsync(scope.ServiceProvider, brandAId, [categoryId], "GL-ACME-1", "قفازات", "Glove", ["ksa"]);
            await SearchTestSeedHelper.CreatePublishedProductAsync(scope.ServiceProvider, brandBId, [categoryId], "GL-DENT-1", "قفازات", "Glove", ["ksa"]);

            await SearchTestSeedHelper.RebuildSearchIndexesAsync(scope.ServiceProvider);
        }

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/customer/search/products", new
        {
            query = "glove",
            marketCode = "ksa",
            locale = "en",
            page = 1,
            pageSize = 24,
            filters = new
            {
                brandIds = new[] { brandA },
            },
            sort = "relevance",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SearchProductsResponseDto>();
        body.Should().NotBeNull();
        body!.Hits.Should().ContainSingle();
        body.Hits[0].Sku.Should().Be("GL-ACME-1");
        body.Hits.Should().NotContain(h => h.Sku == "GL-DENT-1");
        body.Facets.BrandId.Keys.Should().Contain(brandA);
        body.Facets.BrandId.Keys.Should().NotContain(brandB);
    }

    [Fact]
    public async Task SearchProducts_RestrictedProduct_SurfacesWithFlag()
    {
        await factory.ResetDatabaseAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var categoryId = await SearchTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "implants", "زرعات", "Implants");
            var brandId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme", "اكمي", "Acme");

            await SearchTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider,
                brandId,
                [categoryId],
                "IM-001",
                "زرعة متقدمة",
                "Advanced Implant",
                ["ksa"],
                restricted: true,
                restrictionReasonCode: "professional_verification");

            await SearchTestSeedHelper.RebuildSearchIndexesAsync(scope.ServiceProvider);
        }

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/customer/search/products", new
        {
            query = "implant",
            marketCode = "ksa",
            locale = "en",
            page = 1,
            pageSize = 24,
            filters = new { },
            sort = "relevance",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SearchProductsResponseDto>();
        body.Should().NotBeNull();
        body!.Hits.Should().ContainSingle();
        body.Hits[0].Restricted.Should().BeTrue();
        body.Hits[0].RestrictionReasonCode.Should().Be("professional_verification");
    }

    private sealed record SearchProductsResponseDto(
        IReadOnlyList<SearchProductHitDto> Hits,
        SearchFacetsDto Facets,
        int TotalEstimate,
        int QueryDurationMs,
        int EngineLatencyMs,
        bool LocaleFallbackApplied);

    private sealed record SearchProductHitDto(
        Guid Id,
        string Sku,
        string? Barcode,
        string Name,
        bool Restricted,
        string? RestrictionReasonCode);

    private sealed record SearchFacetsDto(
        IReadOnlyDictionary<string, int> BrandId,
        IReadOnlyDictionary<string, int> CategoryId,
        IReadOnlyDictionary<string, int> PriceBucket,
        IReadOnlyDictionary<string, int> Restricted,
        IReadOnlyDictionary<string, int> Availability);
}
