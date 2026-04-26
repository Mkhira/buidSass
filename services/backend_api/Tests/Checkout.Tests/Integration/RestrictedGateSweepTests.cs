using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Checkout.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Tests.Integration;

/// <summary>
/// T019 / SC-005 — bulk restricted-product sweep. The single-row
/// <see cref="Contract.Customer.RestrictedGateTests"/> already covers the gate semantics; this
/// test amplifies coverage to 100 distinct restricted SKUs with parameterised inputs (SKU
/// identity + qty) to confirm there is no off-by-one or product-id-keyed cache that could let
/// a restricted submit slip through.
///
/// Each iteration: seed a fresh restricted product + cart, take the unverified customer
/// through the full state machine, expect submit → 403 <c>checkout.restricted_not_allowed</c>.
/// Skipping intermediate happy-path verifications keeps the run-time bounded; the assertion
/// surface is the gate itself.
/// </summary>
[Collection("checkout-fixture")]
public sealed class RestrictedGateSweepTests(CheckoutTestFactory factory)
{
    [Fact]
    public async Task RestrictedGate_100Sweep_AllBlocked()
    {
        await factory.ResetDatabaseAsync();

        // Pre-seed shared infra once.
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
            await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-sweep-wh", "ksa");
        }

        const int Iterations = 100;
        var blockedCount = 0;
        var unblockedSkus = new List<string>();

        for (int i = 0; i < Iterations; i++)
        {
            // Fresh customer per iteration so the (account_id, market_code) WHERE active
            // unique-cart index doesn't collide with the previous iteration's blocked cart.
            var (token, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
            var client = factory.CreateClient();
            CheckoutCustomerAuthHelper.SetBearer(client, token);

            var sku = $"SKU-RST-SWP-{i:D3}";
            Guid productId, warehouseId, cartId;
            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(
                    seedScope.ServiceProvider, sku, ["ksa"],
                    restricted: true,
                    restrictionReasonCode: "catalog.restricted.verification_required");
                warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-sweep-wh", "ksa");
                await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 5);
                await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId,
                    $"LOT-{sku}", DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 5);
                cartId = await CheckoutTestSeedHelper.SeedReadyCartAsync(
                    seedScope.ServiceProvider, accountId, "ksa", productId, qty: 1 + (i % 3));
            }

            var startResp = await client.PostAsJsonAsync("/v1/customer/checkout/sessions",
                new { cartId, marketCode = "ksa" });
            if (startResp.StatusCode == HttpStatusCode.Forbidden)
            {
                // Some SKUs gate at start-session via the cart-restricted check — that's still
                // a successful block (FR-009 semantics).
                blockedCount++;
                continue;
            }
            startResp.StatusCode.Should().Be(HttpStatusCode.OK,
                $"iteration {i}: start session for {sku}");
            var sessionId = Guid.Parse((await startResp.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("sessionId").GetString()!);

            await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/address",
                new { shipping = new { fullName = "Dr Test", phoneE164 = "+966501234567", line1 = "1 Test", city = "Riyadh", countryCode = "SA" } });
            var quotesResp = await client.GetAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping-quotes");
            var q = (await quotesResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quotes")[0];
            await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping",
                new
                {
                    providerId = q.GetProperty("providerId").GetString(),
                    methodCode = q.GetProperty("methodCode").GetString(),
                });
            await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/payment-method",
                new { method = "card" });

            using var submitReq = new HttpRequestMessage(HttpMethod.Post,
                $"/v1/customer/checkout/sessions/{sessionId}/submit");
            submitReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            submitReq.Content = JsonContent.Create(new { });
            var submitResp = await client.SendAsync(submitReq);

            if (submitResp.StatusCode == HttpStatusCode.Forbidden)
            {
                var body = await submitResp.Content.ReadAsStringAsync();
                if (body.Contains("checkout.restricted_not_allowed"))
                {
                    blockedCount++;
                    continue;
                }
            }
            unblockedSkus.Add($"{sku} (status={(int)submitResp.StatusCode})");
        }

        unblockedSkus.Should().BeEmpty(
            $"SC-005 requires 100/100 restricted submits to be blocked; leaked: {string.Join(", ", unblockedSkus)}");
        blockedCount.Should().Be(Iterations);
    }
}
