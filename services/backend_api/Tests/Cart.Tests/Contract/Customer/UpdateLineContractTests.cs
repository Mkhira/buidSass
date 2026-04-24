using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Contract.Customer;

[Collection("cart-fixture")]
public sealed class UpdateLineContractTests(CartTestFactory factory)
{
    [Fact]
    public async Task UpdateQty_ExtendsReservation_ReplacesOldOne()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-UP-001", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-up-1", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 20);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-UP",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 20);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var client = factory.CreateClient();
        var addResp = await client.PostAsJsonAsync("/v1/customer/cart/lines",
            new { marketCode = "ksa", productId, qty = 2 });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var lineId = (await addResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("lines")[0].GetProperty("id").GetString();

        var patchResp = await client.PatchAsJsonAsync($"/v1/customer/cart/lines/{lineId}",
            new { marketCode = "ksa", qty = 5 });
        patchResp.StatusCode.Should().Be(HttpStatusCode.OK, because: await patchResp.Content.ReadAsStringAsync());

        var payload = await patchResp.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("lines")[0].GetProperty("qty").GetInt32().Should().Be(5);

        await using var assertScope = factory.Services.CreateAsyncScope();
        var cartDb = assertScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var inventoryDb = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var active = await inventoryDb.InventoryReservations
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == "active")
            .ToListAsync();
        active.Should().ContainSingle(because: "old reservation released + new one issued");
        active[0].Qty.Should().Be(5);
    }

    [Fact]
    public async Task UpdateQtyZero_RemovesLine_ReleasesReservation()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-UP-002", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-up-2", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 20);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-UP2",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 20);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var client = factory.CreateClient();
        var addResp = await client.PostAsJsonAsync("/v1/customer/cart/lines",
            new { marketCode = "ksa", productId, qty = 3 });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var lineId = (await addResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("lines")[0].GetProperty("id").GetString();

        var patchResp = await client.PatchAsJsonAsync($"/v1/customer/cart/lines/{lineId}",
            new { marketCode = "ksa", qty = 0 });
        patchResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await patchResp.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("lines").GetArrayLength().Should().Be(0);
        payload.GetProperty("checkoutEligibility").GetProperty("reasonCode").GetString().Should().Be("cart.empty");

        await using var assertScope = factory.Services.CreateAsyncScope();
        var inventoryDb = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var active = await inventoryDb.InventoryReservations
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == "active")
            .ToListAsync();
        active.Should().BeEmpty(because: "removing the line releases the reservation");
    }

    [Fact]
    public async Task DeleteLine_RemovesLine()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-UP-003", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-up-3", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 10);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-UP3",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 10);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var client = factory.CreateClient();
        var addResp = await client.PostAsJsonAsync("/v1/customer/cart/lines",
            new { marketCode = "ksa", productId, qty = 1 });
        var lineId = (await addResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("lines")[0].GetProperty("id").GetString();

        var delResp = await client.DeleteAsync($"/v1/customer/cart/lines/{lineId}?market=ksa");
        delResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await delResp.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("lines").GetArrayLength().Should().Be(0);
    }
}
