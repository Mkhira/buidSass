using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Checkout.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Tests.Contract.Customer;

/// <summary>US6 / SC-008 — COD cap + restricted-product matrix.</summary>
[Collection("checkout-fixture")]
public sealed class CodContractTests(CheckoutTestFactory factory)
{
    // Values chosen to sweep above + below the KSA 2000 SAR cap (200000 minor).
    [Theory]
    [InlineData("ksa", 50_00, true)]      // 50 SAR (5000 minor) — well under cap
    [InlineData("ksa", 3_000_00, false)]  // 3000 SAR — over 2000 cap
    [InlineData("eg", 100_00, true)]      // 100 EGP — under EG cap
    [InlineData("eg", 10_000_00, false)]  // 10000 EGP — over 5000 cap
    public async Task CodCap_MarketMatrix_Enforced(string market, long pricePerUnitMinor, bool expectAllowed)
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, market);
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(
            seedScope.ServiceProvider, $"SKU-COD-{Guid.NewGuid():N}"[..16], new[] { market }, priceHintMinor: pricePerUnitMinor);
        var warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, $"{market}-cod-wh-{Guid.NewGuid():N}"[..16], market);
        await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, 10);
        await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-COD", DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), 10);
        await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, market);
        var cartId = await CheckoutTestSeedHelper.SeedReadyCartAsync(seedScope.ServiceProvider, accountId, market, productId, qty: 1);

        var client = factory.CreateClient();
        CheckoutCustomerAuthHelper.SetBearer(client, token);
        var start = await client.PostAsJsonAsync("/v1/customer/checkout/sessions", new { cartId, marketCode = market });
        var sessionId = Guid.Parse((await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString()!);
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/address", new { shipping = new { fullName = "Dr Test", phoneE164 = "+966501234567", line1 = "1 Test", city = "Riyadh", countryCode = market == "ksa" ? "SA" : "EG" } });
        var q = (await (await client.GetAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping-quotes")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quotes")[0];
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping", new { providerId = q.GetProperty("providerId").GetString(), methodCode = q.GetProperty("methodCode").GetString() });

        var resp = await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/payment-method", new { method = "cod" });
        if (expectAllowed)
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK, because: $"market={market} total={pricePerUnitMinor} within cap — should succeed");
        }
        else
        {
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await resp.Content.ReadAsStringAsync()).Should().Contain("checkout.cod_cap_exceeded");
        }
    }
}
