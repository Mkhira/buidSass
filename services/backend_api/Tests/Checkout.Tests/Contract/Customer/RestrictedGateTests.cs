using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Checkout.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Tests.Contract.Customer;

/// <summary>US3 — restricted-product gate at submit (FR-009 / SC-005).</summary>
[Collection("checkout-fixture")]
public sealed class RestrictedGateTests(CheckoutTestFactory factory)
{
    [Fact]
    public async Task Submit_UnverifiedWithRestricted_Returns403()
    {
        await factory.ResetDatabaseAsync();
        // Unverified customer (default status).
        var (token, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(
            seedScope.ServiceProvider, "SKU-RST-CHK", ["ksa"],
            restricted: true,
            restrictionReasonCode: "catalog.restricted.verification_required");
        var warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-rst-chk", "ksa");
        await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 5);
        await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-RST-CHK",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 5);
        await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
        var cartId = await CheckoutTestSeedHelper.SeedReadyCartAsync(seedScope.ServiceProvider, accountId, "ksa", productId, qty: 1);

        var client = factory.CreateClient();
        CheckoutCustomerAuthHelper.SetBearer(client, token);
        var startResp = await client.PostAsJsonAsync("/v1/customer/checkout/sessions", new { cartId, marketCode = "ksa" });
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionId = Guid.Parse((await startResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString()!);
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/address", new { shipping = new { fullName = "Dr Test", phoneE164 = "+966501234567", line1 = "1 Test", city = "Riyadh", countryCode = "SA" } });
        var quotesResp = await client.GetAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping-quotes");
        var q = (await quotesResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quotes")[0];
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping", new { providerId = q.GetProperty("providerId").GetString(), methodCode = q.GetProperty("methodCode").GetString() });
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/payment-method", new { method = "card" });

        using var submitReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/customer/checkout/sessions/{sessionId}/submit");
        submitReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        submitReq.Content = JsonContent.Create(new { });
        var submitResp = await client.SendAsync(submitReq);
        submitResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await submitResp.Content.ReadAsStringAsync()).Should().Contain("checkout.restricted_not_allowed");
    }
}
