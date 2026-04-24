using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Integration;

[Collection("cart-fixture")]
public sealed class ReservationLifecycleTests(CartTestFactory factory)
{
    [Fact]
    public async Task AddLine_ReservesInventory_AndAttachesReservationId()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-RES-001", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-res-1", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 50);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-R",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 50);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/customer/cart/lines", new
        {
            marketCode = "ksa",
            productId,
            qty = 3,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: await response.Content.ReadAsStringAsync());

        await using var assertScope = factory.Services.CreateAsyncScope();
        var cartDb = assertScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var inventoryDb = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var line = await cartDb.CartLines.AsNoTracking().SingleAsync(l => l.ProductId == productId);
        line.Qty.Should().Be(3);
        line.ReservationId.Should().NotBeNull();

        // Reservation row exists and is active with qty=3, expiring around now+15m (TTL from InventoryOptions).
        var reservation = await inventoryDb.InventoryReservations
            .AsNoTracking()
            .SingleAsync(r => r.Id == line.ReservationId!.Value);
        reservation.Qty.Should().Be(3);
        reservation.Status.Should().Be("active");
        reservation.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(2));

        // Stock level reserved column reflects the reservation.
        var stock = await inventoryDb.StockLevels.AsNoTracking()
            .SingleAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
        stock.Reserved.Should().Be(3);
    }

    [Fact]
    public async Task AddLine_TwiceSameProduct_ReplacesReservationAtHigherQty()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-RES-002", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-res-2", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 20);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-R2",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 20);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 2 });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Carry over the cart_token cookie so the second call resolves the same cart.
        if (first.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            var cartCookie = cookies.FirstOrDefault(c => c.StartsWith("cart_token=", StringComparison.Ordinal));
            if (cartCookie is not null)
            {
                client.DefaultRequestHeaders.Add("Cookie", cartCookie.Split(';')[0]);
            }
        }

        var second = await client.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 3 });
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertScope = factory.Services.CreateAsyncScope();
        var cartDb = assertScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var inventoryDb = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var lines = await cartDb.CartLines.AsNoTracking().Where(l => l.ProductId == productId).ToListAsync();
        lines.Should().ContainSingle();
        lines[0].Qty.Should().Be(5);

        // Previous reservation released → exactly one active reservation for the cart.
        var activeReservations = await inventoryDb.InventoryReservations.AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == "active")
            .ToListAsync();
        activeReservations.Should().ContainSingle();
        activeReservations[0].Qty.Should().Be(5);
    }

    [Fact]
    public async Task AddLine_InsufficientStock_Returns409WithReasonCode()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-RES-003", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-res-3", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 2);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-R3",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 2);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 10 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("cart.inventory_insufficient");
    }
}
