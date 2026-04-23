using System.Net;
using System.Net.Http.Json;
using Catalog.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Tests.Contract.Customer;

[Collection("catalog-fixture")]
public sealed class ProductBySlugContractTests(CatalogTestFactory factory)
{
    [Fact]
    public async Task GetProductBySlug_NonPublished_Returns404()
    {
        await factory.ResetDatabaseAsync();
        string draftSlug;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var brandId = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
            var productId = await CatalogTestSeedHelper.CreateProductAsync(scope.ServiceProvider, brandId, status: "draft");

            var dbContext = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Catalog.Persistence.CatalogDbContext>();
            draftSlug = dbContext.Products.Single(p => p.Id == productId).SlugEn;
        }

        var client = factory.CreateClient();
        var response = await client.GetAsync($"/v1/customer/catalog/products/{draftSlug}?market=ksa");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetProductBySlug_RestrictedProduct_IncludesRestrictionBadge()
    {
        await factory.ResetDatabaseAsync();
        string slug;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var brandId = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
            var productId = await CatalogTestSeedHelper.CreateProductAsync(
                scope.ServiceProvider,
                brandId,
                status: "published",
                restricted: true,
                restrictionReasonCode: "professional_verification",
                restrictionMarkets: new[] { "ksa" });

            var dbContext = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Catalog.Persistence.CatalogDbContext>();
            slug = dbContext.Products.Single(p => p.Id == productId).SlugEn;
        }

        var client = factory.CreateClient();
        var response = await client.GetAsync($"/v1/customer/catalog/products/{slug}?market=ksa");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProductResponseDto>();
        body.Should().NotBeNull();
        body!.Restriction.Restricted.Should().BeTrue();
        body.Restriction.ReasonCode.Should().Be("professional_verification");
    }

    private sealed record ProductResponseDto(Guid Id, string Sku, string? Barcode, Guid BrandId, RestrictionBadgeDto Restriction);
    private sealed record RestrictionBadgeDto(bool Restricted, string? ReasonCode);
}
