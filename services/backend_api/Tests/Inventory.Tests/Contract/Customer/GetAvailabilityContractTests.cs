using System.Net;
using System.Text.Json;
using FluentAssertions;
using Inventory.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Contract.Customer;

[Collection("inventory-fixture")]
public sealed class GetAvailabilityContractTests(InventoryTestFactory factory)
{
    [Fact]
    public async Task GetAvailability_BatchBuckets_NoRawQty()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var warehouseId = await InventoryTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-main", "ksa");

        var p1 = await InventoryTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-CUST-001", ["ksa"]);
        var p2 = await InventoryTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-CUST-002", ["ksa"]);

        await InventoryTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, p1, warehouseId, onHand: 10, reserved: 0, safetyStock: 0, bucketCache: "in_stock");
        await InventoryTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, p2, warehouseId, onHand: 0, reserved: 0, safetyStock: 0, bucketCache: "out_of_stock");

        var client = factory.CreateClient();
        var response = await client.GetAsync($"/v1/customer/inventory/availability?productIds={p1},{p2}&market=ksa");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;

        root.TryGetProperty("items", out var items).Should().BeTrue();
        items.GetArrayLength().Should().Be(2);

        foreach (var item in items.EnumerateArray())
        {
            item.TryGetProperty("productId", out _).Should().BeTrue();
            item.TryGetProperty("bucket", out _).Should().BeTrue();
            item.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["productId", "bucket"]);
        }
    }
}
