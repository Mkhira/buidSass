using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Contract.Customer;

[Collection("cart-fixture")]
public sealed class RestrictionContractTests(CartTestFactory factory)
{
    [Fact]
    public async Task AddRestricted_AddsWithFlag_EligibilityBlocks()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(
            seedScope.ServiceProvider,
            "SKU-RST-001",
            ["ksa"],
            restricted: true,
            restrictionReasonCode: "catalog.restricted.professional_only");
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-rst-1", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 10);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-RST",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 10);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/customer/cart/lines", new
        {
            marketCode = "ksa",
            productId,
            qty = 1,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var line = payload.GetProperty("lines")[0];
        line.GetProperty("restricted").GetBoolean().Should().BeTrue();
        line.GetProperty("restrictionReasonCode").GetString().Should().Be("catalog.restricted.professional_only");

        // Eligibility blocks because the customer isn't verified — still Principle 8: the line
        // remains visible, the cart is untouched, only checkout is gated.
        var eligibility = payload.GetProperty("checkoutEligibility");
        eligibility.GetProperty("allowed").GetBoolean().Should().BeFalse();
        eligibility.GetProperty("reasonCode").GetString().Should().Be("catalog.restricted.professional_only");
    }
}
