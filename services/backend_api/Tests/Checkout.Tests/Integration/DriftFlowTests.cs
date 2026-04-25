using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Catalog.Persistence;
using Checkout.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Tests.Integration;

/// <summary>
/// T033 / SC-004 — when Pricing differs between Summary (Preview) and Submit (Issue),
/// Submit MUST surface a drift and require an explicit accept-drift before re-submit succeeds.
/// We induce drift by bumping the product's price hint between Summary and the first Submit.
/// </summary>
[Collection("checkout-fixture")]
public sealed class DriftFlowTests(CheckoutTestFactory factory)
{
    [Fact]
    public async Task Drift_ShownAndAccepted_ReSubmitSucceeds()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        Guid productId;
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(
                seedScope.ServiceProvider, "SKU-DRIFT", ["ksa"], priceHintMinor: 10_000);
            var warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-drift", "ksa");
            await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, 5);
            await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-DRIFT",
                DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), 5);
            await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
            _ = await CheckoutTestSeedHelper.SeedReadyCartAsync(seedScope.ServiceProvider, accountId, "ksa", productId, qty: 1);
        }

        await using var setupScope = factory.Services.CreateAsyncScope();
        var cartDb = setupScope.ServiceProvider.GetRequiredService<BackendApi.Modules.Cart.Persistence.CartDbContext>();
        var cartId = await cartDb.Carts.Where(c => c.AccountId == accountId).Select(c => c.Id).SingleAsync();

        var client = factory.CreateClient();
        CheckoutCustomerAuthHelper.SetBearer(client, token);
        var sessionId = await WalkToPaymentSelectedAsync(client, cartId);

        // Lock in the preview hash at the original price via Summary.
        var summaryResp = await client.GetAsync($"/v1/customer/checkout/sessions/{sessionId}/summary");
        summaryResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var summaryPayload = await summaryResp.Content.ReadFromJsonAsync<JsonElement>();
        var originalGrandTotal = summaryPayload.GetProperty("pricing").GetProperty("grandTotalMinor").GetInt64();

        // Induce drift by raising the price between preview and submit.
        await using (var mutateScope = factory.Services.CreateAsyncScope())
        {
            var catalogDb = mutateScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await catalogDb.Products.Where(p => p.Id == productId)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.PriceHintMinorUnits, 20_000L));
        }

        // First submit — Pricing Issue recomputes a higher total, drift fires 409.
        using var firstReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/customer/checkout/sessions/{sessionId}/submit");
        firstReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        firstReq.Content = JsonContent.Create(new { });
        var firstResp = await client.SendAsync(firstReq);
        firstResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var firstBody = await firstResp.Content.ReadAsStringAsync();
        firstBody.Should().Contain("checkout.pricing_drift");

        var problem = JsonDocument.Parse(firstBody).RootElement;
        var newGrandTotal = problem.GetProperty("newGrandTotalMinor").GetInt64();
        newGrandTotal.Should().BeGreaterThan(originalGrandTotal, because: "price bumped 2x");

        // Accept the new total.
        var acceptResp = await client.PostAsJsonAsync(
            $"/v1/customer/checkout/sessions/{sessionId}/accept-drift",
            new { acceptedTotalMinor = newGrandTotal });
        acceptResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Re-submit with the accepted total — clears drift gate and confirms the order.
        using var secondReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/customer/checkout/sessions/{sessionId}/submit");
        secondReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        secondReq.Content = JsonContent.Create(new { acceptedTotalMinor = newGrandTotal });
        var secondResp = await client.SendAsync(secondReq);
        var secondBody = await secondResp.Content.ReadAsStringAsync();
        secondResp.StatusCode.Should().Be(HttpStatusCode.OK, because: secondBody);
        JsonDocument.Parse(secondBody).RootElement.GetProperty("orderId").GetString().Should().NotBeNullOrEmpty();
    }

    private static async Task<Guid> WalkToPaymentSelectedAsync(HttpClient client, Guid cartId)
    {
        var start = await client.PostAsJsonAsync("/v1/customer/checkout/sessions", new { cartId, marketCode = "ksa" });
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionId = Guid.Parse((await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString()!);
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/address", new
        {
            shipping = new { fullName = "Dr Drift", phoneE164 = "+966501234567", line1 = "1 Drift", city = "Riyadh", countryCode = "SA" },
        });
        var q = (await (await client.GetAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping-quotes")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quotes")[0];
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping", new
        {
            providerId = q.GetProperty("providerId").GetString(),
            methodCode = q.GetProperty("methodCode").GetString(),
        });
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/payment-method", new { method = "card" });
        return sessionId;
    }
}
