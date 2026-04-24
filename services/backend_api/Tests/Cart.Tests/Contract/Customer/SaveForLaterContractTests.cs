using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Inventory.Persistence;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Contract.Customer;

[Collection("cart-fixture")]
public sealed class SaveForLaterContractTests(CartTestFactory factory)
{
    [Fact]
    public async Task MoveToSaved_ReleasesReservationAndMovesLine()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-SFL-001", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-sfl-1", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 10);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-SFL",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 10);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var (accessToken, _) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var client = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(client, accessToken);

        var addResp = await client.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 2 });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var moveResp = await client.PostAsJsonAsync("/v1/customer/cart/saved-items", new { marketCode = "ksa", productId });
        moveResp.StatusCode.Should().Be(HttpStatusCode.OK, because: await moveResp.Content.ReadAsStringAsync());

        var payload = await moveResp.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("lines").GetArrayLength().Should().Be(0);
        payload.GetProperty("savedItems").GetArrayLength().Should().Be(1);
        // Qty preserved per M7.
        payload.GetProperty("savedItems")[0].GetProperty("qty").GetInt32().Should().Be(2);

        await using var assertScope = factory.Services.CreateAsyncScope();
        var inventoryDb = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var active = await inventoryDb.InventoryReservations.AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == "active").ToListAsync();
        active.Should().BeEmpty(because: "saved-for-later releases the reservation");
    }

    [Fact]
    public async Task RestoreFromSaved_ReservesAndMovesBackAtPreservedQty()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-SFL-002", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-sfl-2", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 10);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-SFL2",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 10);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var (accessToken, _) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var client = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(client, accessToken);

        await client.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 3 });
        await client.PostAsJsonAsync("/v1/customer/cart/saved-items", new { marketCode = "ksa", productId });

        var restoreResp = await client.PostAsJsonAsync($"/v1/customer/cart/saved-items/{productId}/restore?market=ksa", new { });
        restoreResp.StatusCode.Should().Be(HttpStatusCode.OK, because: await restoreResp.Content.ReadAsStringAsync());

        var payload = await restoreResp.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("lines").GetArrayLength().Should().Be(1);
        payload.GetProperty("savedItems").GetArrayLength().Should().Be(0);
        payload.GetProperty("lines")[0].GetProperty("qty").GetInt32().Should().Be(3, because: "restore preserves the original qty");
    }
}
