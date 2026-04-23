using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Search.Tests.Infrastructure;

namespace Search.Tests.Integration;

[Collection("search-fixture")]
public sealed class ReindexLiveTests(SearchTestFactory factory)
{
    [Fact]
    public async Task Reindex_WhileLive_NoEventLoss()
    {
        await factory.ResetDatabaseAsync();

        Guid brandId;
        Guid categoryId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            categoryId = await SearchTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "reindex-live", "مباشر", "Live");
            brandId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme", "اكمي", "Acme");

            await SearchTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider,
                brandId,
                [categoryId],
                "LIVE-BASE-001",
                "منتج اساسي",
                "Base Product",
                ["ksa"]);
        }

        var token = await SearchAdminAuthHelper.IssueAdminTokenAsync(factory, ["search.reindex", "search.read"]);
        var client = factory.CreateClient();
        SearchAdminAuthHelper.SetBearer(client, token);

        var start = await client.PostAsync("/v1/admin/search/reindex?index=products-ksa-en", content: null);
        start.StatusCode.Should().Be(HttpStatusCode.Accepted);

        Guid newProductId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            newProductId = await SearchTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider,
                brandId,
                [categoryId],
                "LIVE-NEW-001",
                "منتج جديد",
                "Live New Product",
                ["ksa"]);

            var db = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Catalog.Persistence.CatalogDbContext>();
            var outbox = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Catalog.Primitives.Outbox.CatalogOutboxWriter>();
            outbox.Enqueue(
                "catalog.product.published",
                newProductId,
                new { id = newProductId, sku = "LIVE-NEW-001", marketCodes = new[] { "ksa" }, restricted = false });
            await db.SaveChangesAsync();
        }

        SearchProductsResponseDto? body = null;
        for (var i = 0; i < 100; i++)
        {
            var search = await client.PostAsJsonAsync("/v1/customer/search/products", new
            {
                query = "product",
                marketCode = "ksa",
                locale = "en",
                page = 1,
                pageSize = 24,
                filters = new { },
                sort = "relevance",
            });

            body = await search.Content.ReadFromJsonAsync<SearchProductsResponseDto>();
            if (body?.Hits.Any(h => h.Sku == "LIVE-BASE-001") == true
                && body.Hits.Any(h => h.Sku == "LIVE-NEW-001"))
            {
                break;
            }

            await Task.Delay(200);
        }

        body.Should().NotBeNull();
        body!.Hits.Should().Contain(h => h.Sku == "LIVE-BASE-001");
        body.Hits.Should().Contain(h => h.Sku == "LIVE-NEW-001");
    }

    private sealed record SearchProductsResponseDto(IReadOnlyList<SearchProductHitDto> Hits, object Facets, int TotalEstimate, int QueryDurationMs, int EngineLatencyMs, bool LocaleFallbackApplied);
    private sealed record SearchProductHitDto(Guid Id, string Sku, string? Barcode, string Name, bool Restricted, string? RestrictionReasonCode);
}
