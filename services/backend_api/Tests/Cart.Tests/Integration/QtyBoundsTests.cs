using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Catalog.Persistence;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Integration;

/// <summary>
/// FR-007: min_order_qty + max_per_order from catalog must gate add/update. Fixture seeds
/// a product with both bounds set and asserts the cart endpoint returns cart.below_min_qty /
/// cart.above_max_qty with 400.
/// </summary>
[Collection("cart-fixture")]
public sealed class QtyBoundsTests(CartTestFactory factory)
{
    [Fact]
    public async Task AddLine_BelowMinOrderQty_RejectedWithReasonCode()
    {
        await factory.ResetDatabaseAsync();
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(
            seedScope.ServiceProvider, "SKU-QTY-001", ["ksa"]);
        // Patch min_order_qty = 5 via direct EF write (seed helper doesn't expose it).
        await SetBoundsAsync(seedScope.ServiceProvider, productId, min: 5, max: 0);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-qty-1", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 20);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-QTY",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 20);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/v1/customer/cart/lines",
            new { marketCode = "ksa", productId, qty = 2 });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("cart.below_min_qty");
    }

    [Fact]
    public async Task AddLine_AboveMaxPerOrder_RejectedWithReasonCode()
    {
        await factory.ResetDatabaseAsync();
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(
            seedScope.ServiceProvider, "SKU-QTY-002", ["ksa"]);
        await SetBoundsAsync(seedScope.ServiceProvider, productId, min: 0, max: 3);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-qty-2", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 20);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-QTY-2",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 20);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/v1/customer/cart/lines",
            new { marketCode = "ksa", productId, qty = 5 });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("cart.above_max_qty");
    }

    [Fact]
    public async Task Merge_SumCapsAtMaxPerOrder_EmitsNotice()
    {
        // FR-002 merge with max_per_order cap: anon qty 3 + auth qty 3 = 6, max=4 → cap at 4
        // and emit cart.merge.qty_capped notice.
        await factory.ResetDatabaseAsync();
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(
            seedScope.ServiceProvider, "SKU-QTY-MRG", ["ksa"]);
        await SetBoundsAsync(seedScope.ServiceProvider, productId, min: 0, max: 4);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-qty-m", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 50);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-MRG",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 50);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var anon = factory.CreateClient();
        var firstResp = await anon.PostAsJsonAsync("/v1/customer/cart/lines",
            new { marketCode = "ksa", productId, qty = 3 });
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cartToken = firstResp.Headers.GetValues("X-Cart-Token").Single();

        var (accessToken, _) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var authClient = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(authClient, accessToken);
        var authAdd = await authClient.PostAsJsonAsync("/v1/customer/cart/lines",
            new { marketCode = "ksa", productId, qty = 3 });
        authAdd.StatusCode.Should().Be(HttpStatusCode.OK);

        var merge = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(merge, accessToken);
        merge.DefaultRequestHeaders.Add("X-Cart-Token", cartToken);
        var mergeResp = await merge.PostAsJsonAsync("/v1/customer/cart/merge", new { marketCode = "ksa" });
        mergeResp.StatusCode.Should().Be(HttpStatusCode.OK, because: await mergeResp.Content.ReadAsStringAsync());

        var payload = await mergeResp.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("lines")[0].GetProperty("qty").GetInt32().Should().Be(4);
        var notices = payload.GetProperty("mergeNotices");
        notices.GetArrayLength().Should().Be(1);
        notices[0].GetProperty("reasonCode").GetString().Should().Be("cart.merge.qty_capped");
        notices[0].GetProperty("requestedQty").GetInt32().Should().Be(6);
        notices[0].GetProperty("appliedQty").GetInt32().Should().Be(4);
    }

    private static async Task SetBoundsAsync(IServiceProvider sp, Guid productId, int min, int max)
    {
        var db = sp.GetRequiredService<CatalogDbContext>();
        var product = await db.Products.SingleAsync(p => p.Id == productId);
        product.MinOrderQty = min;
        product.MaxPerOrder = max;
        await db.SaveChangesAsync();
    }
}
