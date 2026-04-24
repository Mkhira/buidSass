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
public sealed class MergeContractTests(CartTestFactory factory)
{
    [Fact]
    public async Task Merge_SumsQtyAcrossCarts()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-MRG-001", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-mrg-1", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 50);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-M1",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 50);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        // 1. Add 2 to anonymous cart.
        var anonClient = factory.CreateClient();
        var addResp = await anonClient.PostAsJsonAsync("/v1/customer/cart/lines",
            new { marketCode = "ksa", productId, qty = 2 });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cartCookie = addResp.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("cart_token=", StringComparison.Ordinal))
            .Split(';')[0];
        var cartToken = cartCookie["cart_token=".Length..];

        // 2. Create an authenticated customer, add 3 to their cart.
        var (accessToken, _) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var authClient = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(authClient, accessToken);
        var addAuth = await authClient.PostAsJsonAsync("/v1/customer/cart/lines",
            new { marketCode = "ksa", productId, qty = 3 });
        addAuth.StatusCode.Should().Be(HttpStatusCode.OK, because: await addAuth.Content.ReadAsStringAsync());


        // 3. Call merge with the anon token — auth cart must now contain qty=5 for the product.
        var mergeClient = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(mergeClient, accessToken);
        mergeClient.DefaultRequestHeaders.Add("X-Cart-Token", cartToken);

        var mergeResp = await mergeClient.PostAsJsonAsync("/v1/customer/cart/merge",
            new { marketCode = "ksa" });
        var body = await mergeResp.Content.ReadAsStringAsync();
        mergeResp.StatusCode.Should().Be(HttpStatusCode.OK, because: body);

        var payload = JsonDocument.Parse(body).RootElement;
        payload.GetProperty("lines").GetArrayLength().Should().Be(1, because: body);
        payload.GetProperty("lines")[0].GetProperty("qty").GetInt32().Should().Be(5, because: body);

        // Anon cart should be archived or gone.
        await using var assertScope = factory.Services.CreateAsyncScope();
        var db = assertScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var activeCarts = await db.Carts.AsNoTracking().Where(c => c.Status == "active").ToListAsync();
        activeCarts.Should().ContainSingle(c => c.AccountId != null);
    }

    [Fact]
    public async Task Merge_NoAnonToken_ReturnsAuthCartAsIs()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-MRG-002", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-mrg-2", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 10);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-M2",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 10);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var (accessToken, _) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var client = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(client, accessToken);
        await client.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 4 });

        var mergeResp = await client.PostAsJsonAsync("/v1/customer/cart/merge", new { marketCode = "ksa" });
        mergeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await mergeResp.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("lines").GetArrayLength().Should().Be(1);
        payload.GetProperty("lines")[0].GetProperty("qty").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task Merge_Unauthenticated_Returns401()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/v1/customer/cart/merge", new { marketCode = "ksa" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
