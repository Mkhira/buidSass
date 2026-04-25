using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Integration;

/// <summary>
/// Spec 009 edge case 4: "Reservation lost between reads (TTL expired) → on next read, cart
/// attempts re-reservation; if insufficient, line is flagged stockChanged=true for UI."
/// </summary>
[Collection("cart-fixture")]
public sealed class ReservationRehydrationTests(CartTestFactory factory)
{
    [Fact]
    public async Task GetCart_ReservationExpired_RehydratesWithNewId()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-REHY-1", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-rehy-1", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 10);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-REH",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 10);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var client = factory.CreateClient();
        var addResp = await client.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 2 });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cartTokenCookie = addResp.Headers.GetValues("Set-Cookie").First();

        // Expire the reservation in-place: mark it released so the rehydrator treats it as gone.
        await using (var mutateScope = factory.Services.CreateAsyncScope())
        {
            var invDb = mutateScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await invDb.InventoryReservations
                .Where(r => r.ProductId == productId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, "released")
                    .SetProperty(r => r.ReleasedAt, DateTimeOffset.UtcNow));
            // Also free the Reserved column so the rehydrate's re-reserve can pick up the stock.
            await invDb.StockLevels
                .Where(s => s.ProductId == productId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, 0));
        }

        await using (var beforeScope = factory.Services.CreateAsyncScope())
        {
            var cartDb = beforeScope.ServiceProvider.GetRequiredService<CartDbContext>();
            var line = await cartDb.CartLines.AsNoTracking().SingleAsync(l => l.ProductId == productId);
            line.ReservationId.Should().NotBeNull("line still points at the (now-released) reservation");
        }

        // GET rehydrates the reservation.
        using var getReq = new HttpRequestMessage(HttpMethod.Get, "/v1/customer/cart?market=ksa");
        getReq.Headers.Add("Cookie", cartTokenCookie);
        var getResp = await client.SendAsync(getReq);
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var view = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        var lineView = view.GetProperty("lines")[0];
        lineView.GetProperty("stockChanged").GetBoolean().Should().BeFalse();

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyCartDb = verifyScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var refreshed = await verifyCartDb.CartLines.AsNoTracking().SingleAsync(l => l.ProductId == productId);
        refreshed.ReservationId.Should().NotBeNull();

        var invDbFinal = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var newReservation = await invDbFinal.InventoryReservations.AsNoTracking()
            .SingleAsync(r => r.Id == refreshed.ReservationId!.Value);
        newReservation.Status.Should().Be("active");
        newReservation.Qty.Should().Be(2);
    }

    [Fact]
    public async Task GetCart_ReservationExpired_NoStock_FlagsStockChanged()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-REHY-2", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-rehy-2", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 2);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-REH2",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 2);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var client = factory.CreateClient();
        var addResp = await client.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 2 });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cartTokenCookie = addResp.Headers.GetValues("Set-Cookie").First();

        // Release the reservation AND zero out the batch so the rehydrator can't cover the qty.
        await using (var mutateScope = factory.Services.CreateAsyncScope())
        {
            var invDb = mutateScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await invDb.InventoryReservations
                .Where(r => r.ProductId == productId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "released"));
            await invDb.StockLevels
                .Where(s => s.ProductId == productId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.OnHand, 0).SetProperty(x => x.Reserved, 0));
            await invDb.InventoryBatches
                .Where(b => b.ProductId == productId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.QtyOnHand, 0));
        }

        using var getReq = new HttpRequestMessage(HttpMethod.Get, "/v1/customer/cart?market=ksa");
        getReq.Headers.Add("Cookie", cartTokenCookie);
        var getResp = await client.SendAsync(getReq);
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var view = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        var lineView = view.GetProperty("lines")[0];
        lineView.GetProperty("stockChanged").GetBoolean().Should().BeTrue();

        view.GetProperty("checkoutEligibility").GetProperty("allowed").GetBoolean().Should().BeFalse();

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var cartDb = verifyScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var refreshed = await cartDb.CartLines.AsNoTracking().SingleAsync(l => l.ProductId == productId);
        refreshed.ReservationId.Should().BeNull("rehydrator cleared the stale pointer");
        refreshed.StockChanged.Should().BeTrue();
    }
}
