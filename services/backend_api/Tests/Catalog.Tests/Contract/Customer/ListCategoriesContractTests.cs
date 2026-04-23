using System.Net;
using System.Net.Http.Json;
using Catalog.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Tests.Contract.Customer;

[Collection("catalog-fixture")]
public sealed class ListCategoriesContractTests(CatalogTestFactory factory)
{
    [Fact]
    public async Task ListCategories_ReturnsActiveTreeForMarket()
    {
        await factory.ResetDatabaseAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var rootId = await CatalogTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "root", nameEn: "Root");
            await CatalogTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "child", parentId: rootId, nameEn: "Child");
            await CatalogTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "inactive", nameEn: "Inactive", isActive: false);
        }

        var client = factory.CreateClient();
        var response = await client.GetAsync("/v1/customer/catalog/categories?market=ksa");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ListCategoriesResponseDto>();
        body.Should().NotBeNull();
        body!.Categories.Should().HaveCount(1, "inactive categories are excluded");
        body.Categories[0].Slug.Should().Be("root");
        body.Categories[0].Children.Should().HaveCount(1);
        body.Categories[0].Children[0].Slug.Should().Be("child");
        body.Market.Should().Be("ksa");
    }

    private sealed record ListCategoriesResponseDto(IReadOnlyList<CategoryNodeDto> Categories, string Market);
    private sealed record CategoryNodeDto(Guid Id, string Slug, string NameAr, string NameEn, int Depth, IReadOnlyList<CategoryNodeDto> Children);
}
