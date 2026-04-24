using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Checkout.Entities;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using BackendApi.Modules.Shared;
using Checkout.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Checkout.Tests.Integration;

/// <summary>
/// T034 — payment captured but order creation fails: Submit MUST void the authorization
/// and move the session to failed (spec 010 R12 / edge case 9). Fault-injection swaps the
/// <see cref="IOrderFromCheckoutHandler"/> with a handler that returns IsSuccess=false,
/// so we exercise the compensation branch without touching the real stub.
/// </summary>
[Collection("checkout-fixture")]
public sealed class SagaCompensationTests(CheckoutTestFactory factory)
{
    [Fact]
    public async Task PaymentCaptured_OrderCreateFails_VoidScheduled()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-SAGA", ["ksa"]);
            var warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-saga", "ksa");
            await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, 5);
            await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-SAGA",
                DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), 5);
            await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
            _ = await CheckoutTestSeedHelper.SeedReadyCartAsync(seedScope.ServiceProvider, accountId, "ksa", productId, qty: 1);
        }

        await using var ctxScope = factory.Services.CreateAsyncScope();
        var cartDb = ctxScope.ServiceProvider.GetRequiredService<BackendApi.Modules.Cart.Persistence.CartDbContext>();
        var cartId = await cartDb.Carts.Where(c => c.AccountId == accountId).Select(c => c.Id).SingleAsync();

        // Build a second factory whose IOrderFromCheckoutHandler always fails.
        using var failingFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOrderFromCheckoutHandler>();
                services.AddScoped<IOrderFromCheckoutHandler, FailingOrderHandler>();
            });
        });

        var client = failingFactory.CreateClient();
        CheckoutCustomerAuthHelper.SetBearer(client, token);

        var sessionId = await WalkToPaymentSelectedAsync(client, cartId);

        using var submitReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/customer/checkout/sessions/{sessionId}/submit");
        submitReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        submitReq.Content = JsonContent.Create(new { providerToken = "tok-saga" });
        var resp = await client.SendAsync(submitReq);
        resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("checkout.order_create_failed");

        // Session must be failed, attempt must be voided.
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        var session = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        session.State.Should().Be(CheckoutStates.Failed);
        session.FailureReasonCode.Should().NotBeNull();

        var attempt = await db.PaymentAttempts.AsNoTracking()
            .Where(a => a.SessionId == sessionId)
            .OrderByDescending(a => a.CreatedAt)
            .FirstAsync();
        attempt.State.Should().Be(PaymentAttemptStates.Voided,
            because: "card authorization must be voided when downstream order creation fails");
    }

    private static async Task<Guid> WalkToPaymentSelectedAsync(HttpClient client, Guid cartId)
    {
        var start = await client.PostAsJsonAsync("/v1/customer/checkout/sessions", new { cartId, marketCode = "ksa" });
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionId = Guid.Parse((await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString()!);
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/address", new
        {
            shipping = new { fullName = "Dr Saga", phoneE164 = "+966501234567", line1 = "1 Saga", city = "Riyadh", countryCode = "SA" },
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

    private sealed class FailingOrderHandler : IOrderFromCheckoutHandler
    {
        public Task<OrderFromCheckoutResult> CreateAsync(OrderFromCheckoutRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new OrderFromCheckoutResult(
                IsSuccess: false,
                OrderId: null,
                OrderNumber: null,
                PaymentState: null,
                ErrorCode: "orders.simulated_failure",
                ErrorMessage: "Simulated order handler failure for saga compensation test."));
    }
}
