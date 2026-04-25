using System.Net.Http.Json;
using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Tests.Infrastructure;

namespace Orders.Tests.Integration;

/// <summary>
/// B5 fix — Customer Cancel must release inventory (spec 011 US 2). When the cancel runs on
/// a non-captured order, every prior `kind='sale'` movement for the order must be reversed
/// via inventory's return-movement handler.
/// </summary>
[Collection("orders-fixture")]
public sealed class CancelInventoryReleaseTests(OrdersTestFactory factory)
{
    [Fact]
    public async Task Cancel_AuthorizedOrder_PostsReturnMovement()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);

        // Seed inventory: warehouse + stock_level + sale movement against the order.
        var warehouseId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId,
            paymentState: PaymentSm.Authorized, fulfillmentState: FulfillmentSm.NotStarted);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var inv = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            inv.Warehouses.Add(new Warehouse
            {
                Id = warehouseId, Code = "WH-CXL", DisplayName = "Cancel WH",
                MarketCode = "ksa", IsActive = true,
            });
            inv.StockLevels.Add(new StockLevel
            {
                ProductId = productId, WarehouseId = warehouseId,
                OnHand = 50, Reserved = 0, BucketCache = "in_stock",
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            inv.InventoryMovements.Add(new InventoryMovement
            {
                ProductId = productId, WarehouseId = warehouseId, MarketCode = "ksa",
                Kind = "sale", Delta = -3, SourceKind = "order", SourceId = order.Id,
                ActorAccountId = accountId, OccurredAt = DateTimeOffset.UtcNow,
            });
            await inv.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        OrdersCustomerAuthHelper.SetBearer(client, token);
        var response = await client.PostAsJsonAsync($"/v1/customer/orders/{order.Id}/cancel",
            new { reason = "test cancel" });
        response.EnsureSuccessStatusCode();

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var inventoryDb = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var movements = await inventoryDb.InventoryMovements.AsNoTracking()
            .Where(m => m.SourceId == order.Id)
            .OrderBy(m => m.OccurredAt)
            .ToListAsync();
        // Original sale (-3) plus a return movement (+3) → net delta zero.
        movements.Should().HaveCountGreaterOrEqualTo(2);
        movements.Sum(m => m.Delta).Should().Be(0);
        movements.Should().Contain(m => m.Kind == "return" && m.Delta == 3);
    }
}
