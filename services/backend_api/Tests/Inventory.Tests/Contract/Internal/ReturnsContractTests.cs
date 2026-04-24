using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Inventory.Persistence;
using FluentAssertions;
using Inventory.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Contract.Internal;

[Collection("inventory-fixture")]
public sealed class ReturnsContractTests(InventoryTestFactory factory)
{
    [Fact]
    public async Task Return_RestocksBatch()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await InventoryTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-RET-001", ["ksa"]);
        var warehouseId = await InventoryTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-main", "ksa");
        var batchId = await InventoryTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-RET", new DateOnly(2028, 6, 30), 0);
        await InventoryTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 0, reserved: 0, safetyStock: 0, bucketCache: "out_of_stock");

        var (token, accountId) = await InventoryAdminAuthHelper.IssueAdminTokenAsync(factory, ["inventory.internal.return"]);
        var client = factory.CreateClient();
        InventoryAdminAuthHelper.SetBearer(client, token);

        var response = await client.PostAsJsonAsync("/v1/internal/inventory/movements/return", new
        {
            orderId = Guid.NewGuid(),
            accountId,
            reasonCode = "inventory.return.customer",
            items = new[]
            {
                new { productId, warehouseId, batchId, qty = 2 }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertScope = factory.Services.CreateAsyncScope();
        var db = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var batch = await db.InventoryBatches.AsNoTracking().SingleAsync(x => x.Id == batchId);
        batch.QtyOnHand.Should().Be(2);

        var stock = await db.StockLevels.AsNoTracking().SingleAsync(x => x.ProductId == productId && x.WarehouseId == warehouseId);
        stock.OnHand.Should().Be(2);

        var movement = await db.InventoryMovements.AsNoTracking()
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(x => x.ProductId == productId && x.WarehouseId == warehouseId && x.Kind == "return");

        movement.Should().NotBeNull();
        movement!.Delta.Should().Be(2);
    }
}
