using System.Net;
using System.Net.Http.Json;
using Catalog.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Tests.Contract.Customer;

[Collection("catalog-fixture")]
public sealed class CategoryProductsContractTests(CatalogTestFactory factory)
{
    [Fact]
    public async Task GetCategoryProducts_ReturnsPublishedOnly()
    {
        await factory.ResetDatabaseAsync();
        Guid categoryId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            categoryId = await CatalogTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "general");
            var brandId = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");

            await CatalogTestSeedHelper.CreateProductAsync(scope.ServiceProvider, brandId, categoryId, sku: "pub-1", status: "published");
            await CatalogTestSeedHelper.CreateProductAsync(scope.ServiceProvider, brandId, categoryId, sku: "pub-2", status: "published");
            await CatalogTestSeedHelper.CreateProductAsync(scope.ServiceProvider, brandId, categoryId, sku: "draft-1", status: "draft");
            await CatalogTestSeedHelper.CreateProductAsync(scope.ServiceProvider, brandId, categoryId, sku: "archived-1", status: "archived");
        }

        var client = factory.CreateClient();
        var response = await client.GetAsync("/v1/customer/catalog/categories/general/products?market=ksa");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CategoryProductsResponseDto>();
        body.Should().NotBeNull();
        body!.Total.Should().Be(2, "only published products are exposed");
        body.Items.Select(i => i.Sku).Should().BeEquivalentTo(new[] { "pub-1", "pub-2" });
    }

    [Fact]
    public async Task GetCategoryProducts_BrandFacet_ExposesPerBrandCounts()
    {
        await factory.ResetDatabaseAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var categoryId = await CatalogTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "general");
            var brandA = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme", nameEn: "Acme");
            var brandB = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "dent", nameEn: "Dent");
            await CatalogTestSeedHelper.CreateProductAsync(scope.ServiceProvider, brandA, categoryId, sku: "a-1");
            await CatalogTestSeedHelper.CreateProductAsync(scope.ServiceProvider, brandA, categoryId, sku: "a-2");
            await CatalogTestSeedHelper.CreateProductAsync(scope.ServiceProvider, brandB, categoryId, sku: "b-1");
        }

        var client = factory.CreateClient();
        var response = await client.GetAsync("/v1/customer/catalog/categories/general/products?market=ksa");

        var body = await response.Content.ReadFromJsonAsync<CategoryProductsResponseDto>();
        body!.Facets.Brands.Should().Contain(b => b.Slug == "acme" && b.Count == 2);
        body.Facets.Brands.Should().Contain(b => b.Slug == "dent" && b.Count == 1);
    }

    private sealed record CategoryProductsResponseDto(string CategorySlug, string Market, int Page, int PageSize, int Total, IReadOnlyList<ItemDto> Items, FacetsDto Facets);
    private sealed record ItemDto(Guid Id, string Sku, string LocalizedName, string LocalizedSlug, string NameAr, string NameEn, Guid BrandId, long? PriceHintMinorUnits, bool Restricted, string? RestrictionReasonCode);
    private sealed record FacetsDto(IReadOnlyList<BrandBucketDto> Brands, IReadOnlyList<object> PriceBuckets, object Restriction);
    private sealed record BrandBucketDto(Guid Id, string Slug, string NameAr, string NameEn, int Count);
}
