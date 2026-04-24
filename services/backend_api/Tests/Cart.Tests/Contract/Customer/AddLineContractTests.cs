using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Contract.Customer;

[Collection("cart-fixture")]
public sealed class AddLineContractTests(CartTestFactory factory)
{
    [Fact]
    public async Task AddLine_AnonymousCart_CreatesCartAndSetsCookie()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-CART-ADD-001", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-cart-1", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 20);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-A",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 20);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/customer/cart/lines", new
        {
            marketCode = "ksa",
            productId,
            qty = 2,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: await response.Content.ReadAsStringAsync());

        // Server sets both the Set-Cookie header and the X-Cart-Token response header (R1).
        response.Headers.TryGetValues("Set-Cookie", out var setCookie).Should().BeTrue();
        setCookie!.Any(c => c.StartsWith("cart_token=", StringComparison.Ordinal)).Should().BeTrue();
        response.Headers.TryGetValues("X-Cart-Token", out var tokenHeader).Should().BeTrue();
        tokenHeader!.Single().Should().NotBeNullOrWhiteSpace();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("marketCode").GetString().Should().Be("ksa");
        payload.GetProperty("status").GetString().Should().Be("active");
        payload.GetProperty("pricing").GetProperty("currency").GetString().Should().Be("SAR");
        payload.GetProperty("lines").GetArrayLength().Should().Be(1);
        var line = payload.GetProperty("lines")[0];
        line.GetProperty("qty").GetInt32().Should().Be(2);
        line.GetProperty("productId").GetString().Should().Be(productId.ToString());
        line.GetProperty("unavailable").GetBoolean().Should().BeFalse();
        line.GetProperty("priceBreakdown").GetProperty("grossMinor").GetInt64().Should().BeGreaterThan(0);
        payload.GetProperty("checkoutEligibility").GetProperty("allowed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AddLine_MissingProduct_Returns404WithReasonCode()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/customer/cart/lines", new
        {
            marketCode = "ksa",
            productId = Guid.NewGuid(),
            qty = 1,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString().Should().Be("cart.product.not_found");
    }

    [Fact]
    public async Task AddLine_WrongMarket_Returns400WithReasonCode()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-CART-ADD-002", ["ksa"]);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/customer/cart/lines", new
        {
            marketCode = "eg",
            productId,
            qty = 1,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString().Should().Be("cart.product_market_mismatch");
    }

    [Fact]
    public async Task AddLine_QtyZero_Rejected()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-CART-ADD-003", ["ksa"]);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/customer/cart/lines", new
        {
            marketCode = "ksa",
            productId,
            qty = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
