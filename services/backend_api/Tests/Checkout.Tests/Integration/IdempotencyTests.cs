using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Checkout.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Tests.Integration;

/// <summary>SC-002 — duplicate submit with the same idempotency key returns the cached response.</summary>
[Collection("checkout-fixture")]
public sealed class IdempotencyTests(CheckoutTestFactory factory)
{
    [Fact]
    public async Task Submit_IdempotencyKey_ReplaysResponse()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-IDEM", ["ksa"]);
        var warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-idem", "ksa");
        await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, 10);
        await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-IDEM", DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), 10);
        await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
        var cartId = await CheckoutTestSeedHelper.SeedReadyCartAsync(seedScope.ServiceProvider, accountId, "ksa", productId);

        var client = factory.CreateClient();
        CheckoutCustomerAuthHelper.SetBearer(client, token);
        var sessionId = await ReachPaymentSelectedStateAsync(client, cartId);

        var idempotencyKey = Guid.NewGuid().ToString();

        async Task<HttpResponseMessage> SubmitAsync()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/v1/customer/checkout/sessions/{sessionId}/submit");
            req.Headers.Add("Idempotency-Key", idempotencyKey);
            req.Content = JsonContent.Create(new { providerToken = "tok-idem" });
            return await client.SendAsync(req);
        }

        var first = await SubmitAsync();
        var firstBody = await first.Content.ReadAsStringAsync();
        first.StatusCode.Should().Be(HttpStatusCode.OK, because: firstBody);

        var second = await SubmitAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstPayload = JsonDocument.Parse(firstBody).RootElement;
        var secondPayload = JsonDocument.Parse(secondBody).RootElement;
        secondPayload.GetProperty("orderId").GetString()
            .Should().Be(firstPayload.GetProperty("orderId").GetString(),
                because: "idempotent submit within TTL must replay the cached response");
    }

    [Fact]
    public async Task Submit_IdempotencyKey_Reused_WithDifferentBody_Returns422()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-IDEM2", ["ksa"]);
        var warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-idem2", "ksa");
        await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, 10);
        await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-IDEM2", DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), 10);
        await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
        var cartId = await CheckoutTestSeedHelper.SeedReadyCartAsync(seedScope.ServiceProvider, accountId, "ksa", productId);

        var client = factory.CreateClient();
        CheckoutCustomerAuthHelper.SetBearer(client, token);
        var sessionId = await ReachPaymentSelectedStateAsync(client, cartId);

        var idempotencyKey = Guid.NewGuid().ToString();
        using var req1 = new HttpRequestMessage(HttpMethod.Post, $"/v1/customer/checkout/sessions/{sessionId}/submit");
        req1.Headers.Add("Idempotency-Key", idempotencyKey);
        req1.Content = JsonContent.Create(new { providerToken = "tok-A" });
        (await client.SendAsync(req1)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var req2 = new HttpRequestMessage(HttpMethod.Post, $"/v1/customer/checkout/sessions/{sessionId}/submit");
        req2.Headers.Add("Idempotency-Key", idempotencyKey);
        req2.Content = JsonContent.Create(new { providerToken = "tok-B" });
        var resp2 = await client.SendAsync(req2);
        resp2.StatusCode.Should().Be((HttpStatusCode)422);
        (await resp2.Content.ReadAsStringAsync()).Should().Contain("checkout.idempotency_body_mismatch");
    }

    private static async Task<Guid> ReachPaymentSelectedStateAsync(HttpClient client, Guid cartId)
    {
        var start = await client.PostAsJsonAsync("/v1/customer/checkout/sessions", new { cartId, marketCode = "ksa" });
        start.StatusCode.Should().Be(HttpStatusCode.OK, because: await start.Content.ReadAsStringAsync());
        var sessionId = Guid.Parse((await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString()!);
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/address", new
        {
            shipping = new { fullName = "Dr Test", phoneE164 = "+966501234567", line1 = "1 Test", city = "Riyadh", countryCode = "SA" },
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
