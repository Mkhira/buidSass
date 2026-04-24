using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Cart.Entities;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using Checkout.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Tests.Contract.Customer;

/// <summary>US4 — bank transfer PO gate + B2B eligibility.</summary>
[Collection("checkout-fixture")]
public sealed class BankTransferContractTests(CheckoutTestFactory factory)
{
    [Fact]
    public async Task Submit_B2BBankTransfer_NoPo_Returns400()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        await using var seedScope = factory.Services.CreateAsyncScope();
        // Assign B2B tier so the gate passes, but DO NOT set PO metadata on the cart.
        var pricingDb = seedScope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var tier = new B2BTier
        {
            Id = Guid.NewGuid(), Slug = "pro", Name = "Pro",
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        pricingDb.B2BTiers.Add(tier);
        pricingDb.AccountB2BTiers.Add(new AccountB2BTier
        {
            AccountId = accountId, TierId = tier.Id,
            AssignedAt = DateTimeOffset.UtcNow, AssignedByAccountId = accountId,
        });
        await pricingDb.SaveChangesAsync();

        var productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-BT-1", ["ksa"]);
        var warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-bt-1", "ksa");
        await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, 5);
        await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-BT", DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), 5);
        await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
        var cartId = await CheckoutTestSeedHelper.SeedReadyCartAsync(seedScope.ServiceProvider, accountId, "ksa", productId);

        var client = factory.CreateClient();
        CheckoutCustomerAuthHelper.SetBearer(client, token);
        var startResp = await client.PostAsJsonAsync("/v1/customer/checkout/sessions", new { cartId, marketCode = "ksa" });
        var sessionId = Guid.Parse((await startResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString()!);
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/address", new { shipping = new { fullName = "Clinic", phoneE164 = "+966501234567", line1 = "1 Clinic St", city = "Riyadh", countryCode = "SA" } });
        var q = (await (await client.GetAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping-quotes")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quotes")[0];
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping", new { providerId = q.GetProperty("providerId").GetString(), methodCode = q.GetProperty("methodCode").GetString() });

        var resp = await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/payment-method", new { method = "bank_transfer" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("checkout.b2b.po_required");
    }

    [Fact]
    public async Task Submit_B2BBankTransfer_WithPo_OrderCreatedInPending()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        await using var seedScope = factory.Services.CreateAsyncScope();
        var pricingDb = seedScope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var tier = new B2BTier
        {
            Id = Guid.NewGuid(), Slug = "pro", Name = "Pro",
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        pricingDb.B2BTiers.Add(tier);
        pricingDb.AccountB2BTiers.Add(new AccountB2BTier
        {
            AccountId = accountId, TierId = tier.Id,
            AssignedAt = DateTimeOffset.UtcNow, AssignedByAccountId = accountId,
        });
        await pricingDb.SaveChangesAsync();

        var productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-BT-2", ["ksa"]);
        var warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-bt-2", "ksa");
        await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, 5);
        await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-BT-2", DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), 5);
        await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
        var cartId = await CheckoutTestSeedHelper.SeedReadyCartAsync(seedScope.ServiceProvider, accountId, "ksa", productId);

        // Attach PO metadata to the cart.
        var cartDb = seedScope.ServiceProvider.GetRequiredService<CartDbContext>();
        cartDb.CartB2BMetadata.Add(new CartB2BMetadata
        {
            CartId = cartId, MarketCode = "ksa", PoNumber = "PO-2026-0042",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await cartDb.SaveChangesAsync();

        var client = factory.CreateClient();
        CheckoutCustomerAuthHelper.SetBearer(client, token);
        var startResp = await client.PostAsJsonAsync("/v1/customer/checkout/sessions", new { cartId, marketCode = "ksa" });
        var sessionId = Guid.Parse((await startResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString()!);
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/address", new { shipping = new { fullName = "Clinic", phoneE164 = "+966501234567", line1 = "1 Clinic St", city = "Riyadh", countryCode = "SA" } });
        var q = (await (await client.GetAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping-quotes")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quotes")[0];
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping", new { providerId = q.GetProperty("providerId").GetString(), methodCode = q.GetProperty("methodCode").GetString() });
        var payResp = await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/payment-method", new { method = "bank_transfer" });
        payResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var submitReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/customer/checkout/sessions/{sessionId}/submit");
        submitReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        submitReq.Content = JsonContent.Create(new { });
        var submitResp = await client.SendAsync(submitReq);
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK, because: await submitResp.Content.ReadAsStringAsync());
        var payload = await submitResp.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("paymentState").GetString().Should().Be("pending");
    }
}
