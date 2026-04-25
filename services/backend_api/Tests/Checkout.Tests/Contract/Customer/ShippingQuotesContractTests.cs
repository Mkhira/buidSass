using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Checkout.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Tests.Contract.Customer;

/// <summary>US5 — shipping quote endpoint contract.</summary>
[Collection("checkout-fixture")]
public sealed class ShippingQuotesContractTests(CheckoutTestFactory factory)
{
    [Fact]
    public async Task GetQuotes_ValidAddress_ReturnsAtLeastOne()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-SHIP-1", ["ksa"]);
        var warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-ship-1", "ksa");
        await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, 10);
        await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-SHIP", DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), 10);
        await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
        var cartId = await CheckoutTestSeedHelper.SeedReadyCartAsync(seedScope.ServiceProvider, accountId, "ksa", productId);

        var client = factory.CreateClient();
        CheckoutCustomerAuthHelper.SetBearer(client, token);
        var startResp = await client.PostAsJsonAsync("/v1/customer/checkout/sessions", new { cartId, marketCode = "ksa" });
        var sessionId = Guid.Parse((await startResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString()!);
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/address", new
        {
            shipping = new { fullName = "Dr Test", phoneE164 = "+966501234567", line1 = "1 Test", city = "Riyadh", countryCode = "SA" },
        });

        var resp = await client.GetAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping-quotes");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var quotes = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quotes");
        quotes.GetArrayLength().Should().BeGreaterThan(0);
        quotes[0].GetProperty("providerId").GetString().Should().NotBeNullOrWhiteSpace();
        quotes[0].GetProperty("methodCode").GetString().Should().NotBeNullOrWhiteSpace();
        // Free-shipping or pickup quotes are legitimate — only require non-negative fee.
        quotes[0].GetProperty("feeMinor").GetInt64().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task AddressChange_ClearsShippingSelection()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-ADDRCHG", ["ksa"]);
        var warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-addr", "ksa");
        await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, 10);
        await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-ADDR", DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), 10);
        await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
        var cartId = await CheckoutTestSeedHelper.SeedReadyCartAsync(seedScope.ServiceProvider, accountId, "ksa", productId);

        var client = factory.CreateClient();
        CheckoutCustomerAuthHelper.SetBearer(client, token);
        var startResp = await client.PostAsJsonAsync("/v1/customer/checkout/sessions", new { cartId, marketCode = "ksa" });
        var sessionId = Guid.Parse((await startResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString()!);
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/address", new
        {
            shipping = new { fullName = "Dr Test", phoneE164 = "+966501234567", line1 = "1 Test", city = "Riyadh", countryCode = "SA" },
        });
        var quotes = (await (await client.GetAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping-quotes")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quotes");
        var q = quotes[0];
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping", new { providerId = q.GetProperty("providerId").GetString(), methodCode = q.GetProperty("methodCode").GetString() });

        // Change address — should drop the selection.
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/address", new
        {
            shipping = new { fullName = "Dr Test", phoneE164 = "+966501234567", line1 = "2 Different Street", city = "Jeddah", countryCode = "SA" },
        });
        var sumResp = await client.GetAsync($"/v1/customer/checkout/sessions/{sessionId}/summary");
        var sum = await sumResp.Content.ReadFromJsonAsync<JsonElement>();
        sum.GetProperty("shipping").ValueKind.Should().Be(JsonValueKind.Null, because: "selection clears when address changes");
    }
}
