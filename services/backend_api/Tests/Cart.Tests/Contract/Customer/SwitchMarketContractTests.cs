using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Cart.Persistence;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Contract.Customer;

[Collection("cart-fixture")]
public sealed class SwitchMarketContractTests(CartTestFactory factory)
{
    [Fact]
    public async Task SwitchMarket_ArchivesOldCart()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-SW-001", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-sw-1", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 10);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-SW",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 10);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "eg");

        var (accessToken, _) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var client = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(client, accessToken);

        await client.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 2 });

        var switchResp = await client.PostAsJsonAsync("/v1/customer/cart/switch-market",
            new { fromMarket = "ksa", toMarket = "eg" });
        switchResp.StatusCode.Should().Be(HttpStatusCode.OK, because: await switchResp.Content.ReadAsStringAsync());

        var payload = await switchResp.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("marketCode").GetString().Should().Be("eg");
        payload.GetProperty("lines").GetArrayLength().Should().Be(0);

        await using var assertScope = factory.Services.CreateAsyncScope();
        var db = assertScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var archived = await db.Carts.AsNoTracking().SingleAsync(c => c.MarketCode == "ksa");
        archived.Status.Should().Be("archived");
        archived.ArchivedReason.Should().Be("market_switch");
    }
}
