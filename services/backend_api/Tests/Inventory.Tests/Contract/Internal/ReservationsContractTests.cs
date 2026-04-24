using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Inventory.Persistence;
using FluentAssertions;
using Inventory.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Contract.Internal;

[Collection("inventory-fixture")]
public sealed class ReservationsContractTests(InventoryTestFactory factory)
{
    [Fact]
    public async Task CreateReservation_DecrementsAts()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await InventoryTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-RES-001", ["ksa"]);
        var warehouseId = await InventoryTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-main", "ksa");
        _ = await InventoryTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-1", new DateOnly(2028, 6, 30), 10);
        await InventoryTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 10, reserved: 0, safetyStock: 0, bucketCache: "in_stock");

        var (token, accountId) = await InventoryAdminAuthHelper.IssueAdminTokenAsync(factory, ["inventory.internal.reserve"]);
        var client = factory.CreateClient();
        InventoryAdminAuthHelper.SetBearer(client, token);

        var response = await client.PostAsJsonAsync("/v1/internal/inventory/reservations", new
        {
            cartId = Guid.NewGuid(),
            accountId,
            marketCode = "ksa",
            items = new[]
            {
                new { productId, qty = 3 }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertScope = factory.Services.CreateAsyncScope();
        var db = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var stock = await db.StockLevels.SingleAsync(x => x.ProductId == productId && x.WarehouseId == warehouseId);
        (stock.OnHand - stock.Reserved - stock.SafetyStock).Should().Be(7);
    }

    [Fact]
    public async Task ConvertReservation_WritesSaleMovement()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await InventoryTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-CONV-001", ["ksa"]);
        var warehouseId = await InventoryTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-main", "ksa");
        _ = await InventoryTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-2", new DateOnly(2028, 6, 30), 10);
        await InventoryTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 10, reserved: 0, safetyStock: 0, bucketCache: "in_stock");

        var (token, accountId) = await InventoryAdminAuthHelper.IssueAdminTokenAsync(factory, ["inventory.internal.reserve", "inventory.internal.convert"]);
        var client = factory.CreateClient();
        InventoryAdminAuthHelper.SetBearer(client, token);

        var create = await client.PostAsJsonAsync("/v1/internal/inventory/reservations", new
        {
            cartId = Guid.NewGuid(),
            accountId,
            marketCode = "ksa",
            items = new[]
            {
                new { productId, qty = 2 }
            }
        });

        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var createBody = await create.Content.ReadFromJsonAsync<CreateReservationResponse>();
        createBody.Should().NotBeNull();

        var convert = await client.PostAsJsonAsync($"/v1/internal/inventory/reservations/{createBody!.ReservationId}/convert", new
        {
            orderId = Guid.NewGuid(),
            accountId,
        });

        convert.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertScope = factory.Services.CreateAsyncScope();
        var db = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var movement = await db.InventoryMovements
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(x => x.ProductId == productId && x.WarehouseId == warehouseId && x.Kind == "sale");

        movement.Should().NotBeNull();
        movement!.Delta.Should().Be(-2);
    }

    private sealed record CreateReservationResponse(Guid ReservationId);
}
