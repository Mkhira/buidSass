using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Inventory.Persistence;
using FluentAssertions;
using Inventory.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Contract.Admin;

[Collection("inventory-fixture")]
public sealed class BatchesContractTests(InventoryTestFactory factory)
{
    [Fact]
    public async Task ReceiveBatch_WritesMovement()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await InventoryTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-BATCH-001", ["ksa"]);
        var warehouseId = await InventoryTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-main", "ksa");
        await InventoryTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 0, reserved: 0, safetyStock: 0, bucketCache: "out_of_stock");

        var (token, _) = await InventoryAdminAuthHelper.IssueAdminTokenAsync(factory, ["inventory.batch.write", "inventory.batch.read", "inventory.movement.read"]);
        var client = factory.CreateClient();
        InventoryAdminAuthHelper.SetBearer(client, token);

        var response = await client.PostAsJsonAsync("/v1/admin/inventory/batches", new
        {
            productId,
            warehouseId,
            lotNo = "LOT-RECEIPT-001",
            expiryDate = "2028-06-30",
            qty = 12,
            notes = "first receipt"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var assertScope = factory.Services.CreateAsyncScope();
        var db = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var movement = await db.InventoryMovements
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(x => x.ProductId == productId && x.WarehouseId == warehouseId && x.Kind == "receipt");

        movement.Should().NotBeNull();
        movement!.Delta.Should().Be(12);
    }
}
