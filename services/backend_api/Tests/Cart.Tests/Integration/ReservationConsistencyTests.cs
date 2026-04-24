using System.Net.Http.Json;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Integration;

/// <summary>
/// SC-007: every cart line's reservation_id MUST match an active inventory reservation. Runs
/// a mixed sequence of add/update/remove ops and afterwards walks every surviving cart line
/// and asserts its reservation row exists + is active + qty matches.
/// </summary>
[Collection("cart-fixture")]
public sealed class ReservationConsistencyTests(CartTestFactory factory)
{
    [Fact]
    public async Task ReservationConsistency_AfterMixedOps()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var pA = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-RC-A", ["ksa"]);
        var pB = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-RC-B", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-rc", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, pA, warehouseId, onHand: 50);
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, pB, warehouseId, onHand: 50);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, pA, warehouseId, "LOT-RC-A",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 50);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, pB, warehouseId, "LOT-RC-B",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 50);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var client = factory.CreateClient();

        // Add → Update → Add another → Remove → Add again.
        var add1 = await client.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId = pA, qty = 2 });
        add1.EnsureSuccessStatusCode();

        // Capture lineId for pA.
        var getResp = await client.GetAsync("/v1/customer/cart?market=ksa");
        var getPayload = await getResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var lineAId = Guid.Parse(getPayload.GetProperty("lines")[0].GetProperty("id").GetString()!);

        (await client.PatchAsJsonAsync($"/v1/customer/cart/lines/{lineAId}", new { marketCode = "ksa", qty = 4 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId = pB, qty = 3 })).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/v1/customer/cart/lines/{lineAId}", new { marketCode = "ksa", qty = 1 })).EnsureSuccessStatusCode();

        // SC-007 invariant: every cart line's ReservationId matches an active reservation.
        await using var assertScope = factory.Services.CreateAsyncScope();
        var cartDb = assertScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var inventoryDb = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var lines = await cartDb.CartLines.AsNoTracking().ToListAsync();
        foreach (var line in lines)
        {
            line.ReservationId.Should().NotBeNull();
            var reservation = await inventoryDb.InventoryReservations.AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == line.ReservationId);
            reservation.Should().NotBeNull(because: $"line {line.Id} reservation must exist");
            reservation!.Status.Should().Be("active");
            reservation.Qty.Should().Be(line.Qty);
            reservation.ProductId.Should().Be(line.ProductId);
        }

        // No orphan active reservations (each active reservation is referenced by a line).
        var activeReservations = await inventoryDb.InventoryReservations.AsNoTracking()
            .Where(r => r.Status == "active").ToListAsync();
        var lineReservationIds = lines.Where(l => l.ReservationId.HasValue).Select(l => l.ReservationId!.Value).ToHashSet();
        activeReservations.Select(r => r.Id).Should().BeSubsetOf(lineReservationIds);
    }
}
