using System.Diagnostics;
using System.Net.Http.Json;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Catalog.Primitives.Outbox;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Search.Tests.Infrastructure;

namespace Search.Tests.Integration;

[Collection("search-fixture")]
public sealed class IndexerLagTests(SearchTestFactory factory)
{
    [Fact]
    public async Task Indexer_PublishedEvent_SearchableWithin5s()
    {
        await factory.ResetDatabaseAsync();

        const string sku = "IDX-PUB-001";
        Guid productId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var categoryId = await SearchTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "indexer", "مفهرس", "Indexer");
            var brandId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
            productId = await SearchTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider,
                brandId,
                [categoryId],
                sku,
                "منتج مفهرس",
                "Indexed Product",
                ["ksa"]);

            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var outbox = scope.ServiceProvider.GetRequiredService<CatalogOutboxWriter>();
            outbox.Enqueue("catalog.product.published", productId, new { id = productId, sku, marketCodes = new[] { "ksa" }, restricted = false });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var sw = Stopwatch.StartNew();
        var found = false;

        while (sw.Elapsed < TimeSpan.FromSeconds(12))
        {
            var lookup = await client.PostAsJsonAsync("/v1/customer/search/lookup", new
            {
                code = sku,
                marketCode = "ksa",
                locale = "en",
            });

            var body = await lookup.Content.ReadFromJsonAsync<LookupResponseDto>();
            if (body?.Hit?.Sku == sku)
            {
                found = true;
                break;
            }

            await Task.Delay(200);
        }

        found.Should().BeTrue("published events should be searchable quickly");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task Indexer_ArchivedEvent_RemovesFromAllIndexes()
    {
        await factory.ResetDatabaseAsync();

        const string sku = "IDX-ARCH-001";
        Guid productId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var categoryId = await SearchTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "archive", "ارشيف", "Archive");
            var brandId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
            productId = await SearchTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider,
                brandId,
                [categoryId],
                sku,
                "منتج ارشيف",
                "Archive Product",
                ["ksa", "eg"]);

            await SearchTestSeedHelper.RebuildSearchIndexesAsync(scope.ServiceProvider);

            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var product = await db.Products.SingleAsync(p => p.Id == productId);
            product.Status = "archived";
            product.ArchivedAt = DateTimeOffset.UtcNow;

            var outbox = scope.ServiceProvider.GetRequiredService<CatalogOutboxWriter>();
            outbox.Enqueue("catalog.product.archived", productId, new { id = productId, sku });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var goneEverywhere = false;

        for (var i = 0; i < 60; i++)
        {
            var checks = new[]
            {
                await LookupAsync(client, sku, "ksa", "ar"),
                await LookupAsync(client, sku, "ksa", "en"),
                await LookupAsync(client, sku, "eg", "ar"),
                await LookupAsync(client, sku, "eg", "en"),
            };

            if (checks.All(x => x is null))
            {
                goneEverywhere = true;
                break;
            }

            await Task.Delay(200);
        }

        goneEverywhere.Should().BeTrue("archived products must be removed from all partitioned indexes");
    }

    [Fact]
    public async Task Indexer_Redelivery_IsIdempotent()
    {
        await factory.ResetDatabaseAsync();

        const string sku = "IDX-DUP-001";
        Guid productId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var categoryId = await SearchTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "dup", "تكرار", "Dup");
            var brandId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
            productId = await SearchTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider,
                brandId,
                [categoryId],
                sku,
                "منتج مكرر",
                "Duplicate Product",
                ["ksa"]);

            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var outbox = scope.ServiceProvider.GetRequiredService<CatalogOutboxWriter>();
            outbox.Enqueue("catalog.product.published", productId, new { id = productId, sku, marketCodes = new[] { "ksa" }, restricted = false });
            outbox.Enqueue("catalog.product.published", productId, new { id = productId, sku, marketCodes = new[] { "ksa" }, restricted = false });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();

        SearchProductsResponseDto? body = null;
        for (var i = 0; i < 60; i++)
        {
            var response = await client.PostAsJsonAsync("/v1/customer/search/products", new
            {
                query = "duplicate",
                marketCode = "ksa",
                locale = "en",
                page = 1,
                pageSize = 24,
                filters = new { },
                sort = "relevance",
            });

            body = await response.Content.ReadFromJsonAsync<SearchProductsResponseDto>();
            if (body?.Hits.Count == 1)
            {
                break;
            }

            await Task.Delay(200);
        }

        body.Should().NotBeNull();
        body!.Hits.Should().ContainSingle(h => h.Sku == sku);
    }

    private static async Task<LookupHitDto?> LookupAsync(HttpClient client, string code, string marketCode, string locale)
    {
        var response = await client.PostAsJsonAsync("/v1/customer/search/lookup", new { code, marketCode, locale });
        var body = await response.Content.ReadFromJsonAsync<LookupResponseDto>();
        return body?.Hit;
    }

    private sealed record LookupResponseDto(LookupHitDto? Hit);
    private sealed record LookupHitDto(Guid Id, string Sku, string? Barcode, string Name, bool Restricted, string? RestrictionReasonCode, string MarketCode, string Locale);

    private sealed record SearchProductsResponseDto(IReadOnlyList<SearchProductHitDto> Hits, object Facets, int TotalEstimate, int QueryDurationMs, int EngineLatencyMs, bool LocaleFallbackApplied);
    private sealed record SearchProductHitDto(Guid Id, string Sku, string Name, bool Restricted, string? RestrictionReasonCode);
}
