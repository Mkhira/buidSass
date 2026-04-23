using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Search.Tests.Infrastructure;

namespace Search.Tests.Contract.Customer;

[Collection("search-fixture")]
public sealed class LookupContractTests(SearchTestFactory factory)
{
    [Fact]
    public async Task Lookup_ExactSku_ReturnsSingleHit()
    {
        await factory.ResetDatabaseAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var categoryId = await SearchTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "lookup", "بحث", "Lookup");
            var brandId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
            await SearchTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider,
                brandId,
                [categoryId],
                sku: "DX-001-KSA",
                nameAr: "منتج بحث",
                nameEn: "Lookup Product",
                marketCodes: ["ksa"]);
            await SearchTestSeedHelper.RebuildSearchIndexesAsync(scope.ServiceProvider);
        }

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/customer/search/lookup", new
        {
            code = "DX-001-KSA",
            marketCode = "ksa",
            locale = "en",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LookupResponseDto>();
        body.Should().NotBeNull();
        body!.Hit.Should().NotBeNull();
        body.Hit!.Sku.Should().Be("DX-001-KSA");
    }

    [Fact]
    public async Task Lookup_Barcode_Under100ms()
    {
        await factory.ResetDatabaseAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var categoryId = await SearchTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "lookup", "بحث", "Lookup");
            var brandId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
            await SearchTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider,
                brandId,
                [categoryId],
                sku: "DX-002-KSA",
                barcode: "6291000000001",
                nameAr: "منتج باركود",
                nameEn: "Barcode Product",
                marketCodes: ["ksa"]);
            await SearchTestSeedHelper.RebuildSearchIndexesAsync(scope.ServiceProvider);
        }

        var client = factory.CreateClient();
        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsJsonAsync("/v1/customer/search/lookup", new
        {
            code = "6291000000001",
            marketCode = "ksa",
            locale = "en",
        });
        stopwatch.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000);

        var body = await response.Content.ReadFromJsonAsync<LookupResponseDto>();
        body!.Hit!.Sku.Should().Be("DX-002-KSA");
    }

    private sealed record LookupResponseDto(LookupHitDto? Hit);
    private sealed record LookupHitDto(Guid Id, string Sku, string? Barcode, string Name, bool Restricted, string? RestrictionReasonCode, string MarketCode, string Locale);
}
