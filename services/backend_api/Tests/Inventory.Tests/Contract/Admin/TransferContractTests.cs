using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Inventory.Persistence;
using FluentAssertions;
using Inventory.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Contract.Admin;

[Collection("inventory-fixture")]
public sealed class TransferContractTests(InventoryTestFactory factory)
{
    [Fact]
    public async Task Transfer_WritesPairedMovements()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await InventoryTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-TR-001", ["ksa", "eg"]);
        var fromWarehouseId = await InventoryTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-main", "ksa");
        var toWarehouseId = await InventoryTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "eg-main", "eg");

        await InventoryTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, fromWarehouseId, onHand: 10, reserved: 0, safetyStock: 0, bucketCache: "in_stock");
        await InventoryTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, toWarehouseId, onHand: 0, reserved: 0, safetyStock: 0, bucketCache: "out_of_stock");

        var (token, _) = await InventoryAdminAuthHelper.IssueAdminTokenAsync(factory, ["inventory.movement.write", "inventory.movement.read"]);
        var client = factory.CreateClient();
        InventoryAdminAuthHelper.SetBearer(client, token);

        var response = await client.PostAsJsonAsync("/v1/admin/inventory/movements/transfer", new
        {
            productId,
            fromWarehouseId,
            toWarehouseId,
            qty = 3,
            reason = "rebalance"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertScope = factory.Services.CreateAsyncScope();
        var db = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var movements = await db.InventoryMovements
            .AsNoTracking()
            .Where(x => x.ProductId == productId)
            .OrderByDescending(x => x.Id)
            .Take(2)
            .ToListAsync();

        movements.Should().HaveCount(2);
        movements.Count(x => x.Kind == "transfer_out" && x.WarehouseId == fromWarehouseId && x.Delta == -3).Should().Be(1);
        movements.Count(x => x.Kind == "transfer_in" && x.WarehouseId == toWarehouseId && x.Delta == 3).Should().Be(1);
    }
}
