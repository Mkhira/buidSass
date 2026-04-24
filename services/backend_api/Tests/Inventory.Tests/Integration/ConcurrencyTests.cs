using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Inventory.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Integration;

[Collection("inventory-fixture")]
public sealed class ConcurrencyTests(InventoryTestFactory factory)
{
    [Fact]
    public async Task _100Concurrent_Exactly5Succeed()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await InventoryTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-CONC-001", ["ksa"]);
        var warehouseId = await InventoryTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-main", "ksa");
        _ = await InventoryTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-CONC", new DateOnly(2028, 6, 30), 5);
        await InventoryTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 5, reserved: 0, safetyStock: 0, bucketCache: "in_stock");

        var (token, accountId) = await InventoryAdminAuthHelper.IssueAdminTokenAsync(factory, ["inventory.internal.reserve"]);
        var client = factory.CreateClient();
        InventoryAdminAuthHelper.SetBearer(client, token);

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => client.PostAsJsonAsync("/v1/internal/inventory/reservations", new
            {
                cartId = Guid.NewGuid(),
                accountId,
                marketCode = "ksa",
                items = new[]
                {
                    new { productId, qty = 1 }
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        var statuses = tasks.Select(t => t.Result.StatusCode).ToArray();
        statuses.Count(s => s == HttpStatusCode.OK).Should().Be(5);
        statuses.Count(s => s == HttpStatusCode.Conflict).Should().Be(95);
    }
}
