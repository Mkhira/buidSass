using BackendApi.Modules.Inventory.Persistence;
using FluentAssertions;
using Inventory.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Integration;

[Collection("inventory-fixture")]
public sealed class ExpiryWriteoffTests(InventoryTestFactory factory)
{
    [Fact]
    public async Task ExpiryWorker_WritesOffExpiredBatches()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await InventoryTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-EXP-001", ["ksa"]);
        var warehouseId = await InventoryTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-main", "ksa");
        await InventoryTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 5, reserved: 0, safetyStock: 0, bucketCache: "in_stock");
        var expiredBatchId = await InventoryTestSeedHelper.AddBatchAsync(
            seedScope.ServiceProvider,
            productId,
            warehouseId,
            "LOT-EXPIRED",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-2)),
            qtyOnHand: 3);

        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(25);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            await using var assertScope = factory.Services.CreateAsyncScope();
            var db = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var batch = await db.InventoryBatches.AsNoTracking().SingleAsync(x => x.Id == expiredBatchId);
            var stock = await db.StockLevels.AsNoTracking().SingleAsync(x => x.ProductId == productId && x.WarehouseId == warehouseId);
            var movement = await db.InventoryMovements.AsNoTracking().OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(x => x.ProductId == productId && x.WarehouseId == warehouseId && x.Kind == "writeoff");

            if (batch.Status == "expired" && movement is not null)
            {
                movement.Delta.Should().Be(-3);
                stock.OnHand.Should().Be(2);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new Xunit.Sdk.XunitException("Expiry writeoff did not run within expected time window.");
    }
}
