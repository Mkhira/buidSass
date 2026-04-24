using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Inventory.Persistence;
using FluentAssertions;
using Inventory.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Integration;

[Collection("inventory-fixture")]
public sealed class ReorderDebounceTests(InventoryTestFactory factory)
{
    [Fact]
    public async Task ReorderCrossed_EmitsOnce()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await InventoryTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-REORDER-001", ["ksa"]);
        var warehouseId = await InventoryTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-main", "ksa");
        _ = await InventoryTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-REORDER", new DateOnly(2028, 6, 30), 20);
        await InventoryTestSeedHelper.UpsertStockAsync(
            seedScope.ServiceProvider,
            productId,
            warehouseId,
            onHand: 11,
            reserved: 0,
            safetyStock: 0,
            reorderThreshold: 10,
            bucketCache: "in_stock");

        var (token, accountId) = await InventoryAdminAuthHelper.IssueAdminTokenAsync(factory, ["inventory.internal.reserve"]);
        var client = factory.CreateClient();
        InventoryAdminAuthHelper.SetBearer(client, token);

        var first = await client.PostAsJsonAsync("/v1/internal/inventory/reservations", new
        {
            cartId = Guid.NewGuid(),
            accountId,
            marketCode = "ksa",
            items = new[] { new { productId, qty = 3 } }
        });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync("/v1/internal/inventory/reservations", new
        {
            cartId = Guid.NewGuid(),
            accountId,
            marketCode = "ksa",
            items = new[] { new { productId, qty = 1 } }
        });
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertScope = factory.Services.CreateAsyncScope();
        var db = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var count = await db.ReorderAlertDebounceEntries.CountAsync(x => x.ProductId == productId && x.WarehouseId == warehouseId);
        count.Should().Be(1);
    }
}
