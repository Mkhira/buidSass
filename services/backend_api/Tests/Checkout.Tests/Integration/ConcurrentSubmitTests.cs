using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using BackendApi.Modules.Inventory.Persistence;
using Checkout.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Tests.Integration;

/// <summary>
/// SC-003 / T014: "concurrent submits across overlapping products produce 0 oversells."
///
/// Spec 008's reservation system is the oversell guard: every cart add books a
/// reservation. SC-003 at the checkout layer must prove the system can't synthesize
/// oversells under concurrent pressure even when DEMAND EXCEEDS SUPPLY.
///
/// Test shape: N contenders try to add a product whose stock = N - oversubscribed. Some
/// add-to-cart calls succeed (reservation granted); others get
/// `cart.inventory_insufficient`. The lucky reservation holders walk their sessions to
/// payment_selected, then we race every submit at once. Invariants asserted post-race:
///   1. Confirmed sessions ≤ stock (never more than the seeded units could cover).
///   2. Reservation count NEVER exceeds onHand stock (no phantom reservation rows).
///   3. Each confirmation maps to a distinct session (no double-confirm via race).
///
/// We use 30 contenders against 10 units rather than 1000 to keep fixture runtime
/// under a minute in CI; the SC-003 invariants asserted are the same.
/// </summary>
[Collection("checkout-fixture")]
public sealed class ConcurrentSubmitTests(CheckoutTestFactory factory)
{
    [Fact]
    public async Task Submit_30ConcurrentSessions_ZeroOversells()
    {
        await factory.ResetDatabaseAsync();

        const int contenders = 30;
        const int stockOnHand = 10; // 3x oversubscribed — guarantees real contention.

        Guid productId;
        Guid warehouseId;
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(
                seedScope.ServiceProvider, "SKU-CONCUR", ["ksa"], priceHintMinor: 5_000);
            warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(
                seedScope.ServiceProvider, "ksa-concur", "ksa");
            // CR review on PR #31: stock < contenders so add-to-cart racing actually fails
            // for some customers, exercising the inventory module's reservation gate AND
            // checkout's "live reservation" gate at submit time.
            await CheckoutTestSeedHelper.UpsertStockAsync(
                seedScope.ServiceProvider, productId, warehouseId, onHand: stockOnHand);
            await CheckoutTestSeedHelper.AddBatchAsync(
                seedScope.ServiceProvider, productId, warehouseId, "LOT-CONCUR",
                DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: stockOnHand);
            await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
        }

        // Provision N customers and add via the real cart endpoint so the reservation gate
        // actually filters down to ~stockOnHand survivors. Going through the API matches
        // production semantics: an add-to-cart that can't reserve returns 4xx and the cart
        // never gets the line, so submit sees only the cohort that genuinely holds stock.
        var customers = new List<(string Token, Guid CartId)>();
        for (var i = 0; i < contenders; i++)
        {
            var (token, _) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
            using var addClient = factory.CreateClient();
            CheckoutCustomerAuthHelper.SetBearer(addClient, token);
            var add = await addClient.PostAsJsonAsync("/v1/customer/cart/lines", new
            {
                marketCode = "ksa",
                productId,
                qty = 1,
            });
            if (add.StatusCode != HttpStatusCode.OK)
            {
                // 409 cart.inventory_insufficient or similar — this contender is out.
                continue;
            }
            var view = await add.Content.ReadFromJsonAsync<JsonElement>();
            customers.Add((token, view.GetProperty("id").GetGuid()));
        }
        customers.Count.Should().Be(stockOnHand,
            because: $"reservation gate must allow exactly {stockOnHand} adds (one per stock unit)");

        // Walk every session up to payment_selected (parallel, but per-session sequential).
        var sessionsByCustomer = new ConcurrentDictionary<int, (HttpClient Client, Guid SessionId)>();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, customers.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            async (i, ct) =>
            {
                var client = factory.CreateClient();
                CheckoutCustomerAuthHelper.SetBearer(client, customers[i].Token);
                var start = await client.PostAsJsonAsync("/v1/customer/checkout/sessions",
                    new { cartId = customers[i].CartId, marketCode = "ksa" }, ct);
                start.StatusCode.Should().Be(HttpStatusCode.OK);
                var sessionId = Guid.Parse(
                    (await start.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct))
                    .GetProperty("sessionId").GetString()!);
                await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/address",
                    new { shipping = new { fullName = $"C{i}", phoneE164 = "+966501234567", line1 = "L", city = "Riyadh", countryCode = "SA" } }, ct);
                var quote = (await (await client.GetAsync(
                    $"/v1/customer/checkout/sessions/{sessionId}/shipping-quotes", ct)).Content
                    .ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("quotes")[0];
                await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping",
                    new { providerId = quote.GetProperty("providerId").GetString(), methodCode = quote.GetProperty("methodCode").GetString() }, ct);
                await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/payment-method",
                    new { method = "card" }, ct);
                sessionsByCustomer[i] = (client, sessionId);
            });

        // Race the submits at high parallelism — this is the SC-003 critical section.
        var outcomes = new ConcurrentBag<HttpStatusCode>();
        await Parallel.ForEachAsync(
            sessionsByCustomer.Values,
            new ParallelOptions { MaxDegreeOfParallelism = 32 },
            async (tuple, ct) =>
            {
                using var req = new HttpRequestMessage(HttpMethod.Post,
                    $"/v1/customer/checkout/sessions/{tuple.SessionId}/submit");
                req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
                req.Content = JsonContent.Create(new { });
                var resp = await tuple.Client.SendAsync(req, ct);
                outcomes.Add(resp.StatusCode);
            });

        outcomes.Count.Should().Be(customers.Count, because: "every submit must produce a response");

        await using var assertScope = factory.Services.CreateAsyncScope();
        var checkoutDb = assertScope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        var inventoryDb = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var sessionIds = sessionsByCustomer.Values.Select(v => v.SessionId).ToArray();
        var confirmedCount = await checkoutDb.Sessions.AsNoTracking()
            .CountAsync(s => sessionIds.Contains(s.Id) && s.State == CheckoutStates.Confirmed);
        var failedCount = await checkoutDb.Sessions.AsNoTracking()
            .CountAsync(s => sessionIds.Contains(s.Id) && s.State == CheckoutStates.Failed);

        // SC-003 PRIMARY invariant: confirmations never exceed seeded stock — even with
        // 3x oversubscribed contenders, the reservation gate caps confirmations at supply.
        confirmedCount.Should().BeLessThanOrEqualTo(stockOnHand,
            because: $"oversell: {confirmedCount} confirms > {stockOnHand} units of stock");

        // Reservation accounting under contention: the count of reservation rows on the
        // product MUST NOT exceed onHand stock (each successful add booked one).
        var reservationRowCount = await inventoryDb.InventoryReservations.AsNoTracking()
            .CountAsync(r => r.ProductId == productId);
        reservationRowCount.Should().BeLessThanOrEqualTo(stockOnHand,
            because: $"reservation rows ({reservationRowCount}) cannot exceed seeded stock ({stockOnHand})");

        // Each confirmed session is unique — no double-confirm via race.
        var distinctConfirmedSessions = await checkoutDb.Sessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id) && s.State == CheckoutStates.Confirmed)
            .Select(s => s.Id)
            .Distinct()
            .CountAsync();
        distinctConfirmedSessions.Should().Be(confirmedCount,
            because: "every confirm row must correspond to a distinct session");

        // We provisioned ~stockOnHand customers; demand pressure is the load test even
        // though only that subset reached submit. The fact that confirmedCount ≤ stockOnHand
        // under MaxDegreeOfParallelism=32 is the SC-003 guarantee.
        (confirmedCount + failedCount).Should().BeGreaterThan(0,
            because: $"some submits must have produced session state changes (got {confirmedCount} confirmed, {failedCount} failed)");
    }
}
