using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using Checkout.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Tests.Contract.Customer;

[Collection("checkout-fixture")]
public sealed class SubmitContractTests(CheckoutTestFactory factory)
{
    [Fact]
    public async Task Submit_HappyPath_ConfirmsOrder()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-SUB-001", ["ksa"]);
        var warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-sub-1", "ksa");
        await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 20);
        await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-SUB",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 20);
        await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
        var cartId = await CheckoutTestSeedHelper.SeedReadyCartAsync(seedScope.ServiceProvider, accountId, "ksa", productId, qty: 2);

        var client = factory.CreateClient();
        CheckoutCustomerAuthHelper.SetBearer(client, token);

        // Walk the session through the state machine.
        var startResp = await client.PostAsJsonAsync("/v1/customer/checkout/sessions", new { cartId, marketCode = "ksa" });
        startResp.StatusCode.Should().Be(HttpStatusCode.OK, because: await startResp.Content.ReadAsStringAsync());
        var sessionId = Guid.Parse((await startResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString()!);

        var addr = new { fullName = "Dr Test", phoneE164 = "+966501234567", line1 = "1 Test Street", city = "Riyadh", countryCode = "SA" };
        var addrResp = await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/address", new { shipping = addr });
        addrResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var quotesResp = await client.GetAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping-quotes");
        quotesResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var quotes = (await quotesResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quotes");
        quotes.GetArrayLength().Should().BeGreaterThan(0);
        var q = quotes[0];

        var shipResp = await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping", new
        {
            providerId = q.GetProperty("providerId").GetString(),
            methodCode = q.GetProperty("methodCode").GetString(),
        });
        shipResp.StatusCode.Should().Be(HttpStatusCode.OK, because: await shipResp.Content.ReadAsStringAsync());

        var payResp = await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/payment-method", new { method = "card" });
        payResp.StatusCode.Should().Be(HttpStatusCode.OK, because: await payResp.Content.ReadAsStringAsync());

        var summaryResp = await client.GetAsync($"/v1/customer/checkout/sessions/{sessionId}/summary");
        summaryResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var submitReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/customer/checkout/sessions/{sessionId}/submit");
        submitReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        submitReq.Content = JsonContent.Create(new { providerToken = "tok-test" });
        var submitResp = await client.SendAsync(submitReq);
        var body = await submitResp.Content.ReadAsStringAsync();
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK, because: body);
        var submitPayload = JsonDocument.Parse(body).RootElement;
        submitPayload.GetProperty("orderId").GetString().Should().NotBeNullOrEmpty();
        submitPayload.GetProperty("paymentState").GetString().Should().Be("captured");

        // Session should be confirmed, cart should be merged.
        await using var assertScope = factory.Services.CreateAsyncScope();
        var checkoutDb = assertScope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        var cartDb = assertScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var session = await checkoutDb.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        session.State.Should().Be(CheckoutStates.Confirmed);
        session.OrderId.Should().NotBeNull();
        var cart = await cartDb.Carts.AsNoTracking().SingleAsync(c => c.Id == cartId);
        cart.Status.Should().Be(CartStatuses.Merged);
    }

    [Fact]
    public async Task Submit_MissingIdempotencyKey_Returns400()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var client = factory.CreateClient();
        CheckoutCustomerAuthHelper.SetBearer(client, token);
        var resp = await client.PostAsJsonAsync($"/v1/customer/checkout/sessions/{Guid.NewGuid()}/submit", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("checkout.idempotency_required");
    }
}
