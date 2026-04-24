using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Cart.Persistence;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Integration;

/// <summary>
/// SC-003: 100-scenario merge determinism check. This is the integration-level counterpart to
/// CartMergerTests; here we drive merges through the real HTTP surface + Postgres so we also
/// cover CartResolver lookup, reservation re-play, and anon-cart archival alongside the merge
/// arithmetic. The scenarios are small (1–3 products, qty 1–4) so the whole suite stays under
/// the Testcontainers budget; the heavy combinatorics live in the unit test.
/// </summary>
[Collection("cart-fixture")]
public sealed class MergeDeterminismTests(CartTestFactory factory)
{
    [Fact]
    public async Task Merge_20Scenarios_SumsQtyAndArchivesAnonCart()
    {
        // 20 scenarios × (seed + anon adds + auth adds + merge + assert) keeps total time bounded
        // while still covering overlap/disjoint/duplicate-product permutations. Unit-level SC-003
        // still runs its full 100 scenarios in CartMergerTests.
        var rng = new Random(20260424);
        for (var scenario = 0; scenario < 20; scenario++)
        {
            await factory.ResetDatabaseAsync();

            await using var seedScope = factory.Services.CreateAsyncScope();
            await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
            var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(
                seedScope.ServiceProvider, $"ksa-det-{scenario}", "ksa");

            // 2 products shared between anon + auth, 1 anon-only, 1 auth-only.
            var sharedA = await MakeProduct(seedScope.ServiceProvider, warehouseId, $"SHR-A-{scenario}");
            var sharedB = await MakeProduct(seedScope.ServiceProvider, warehouseId, $"SHR-B-{scenario}");
            var anonOnly = await MakeProduct(seedScope.ServiceProvider, warehouseId, $"ANON-{scenario}");
            var authOnly = await MakeProduct(seedScope.ServiceProvider, warehouseId, $"AUTH-{scenario}");

            var anonShrAQty = rng.Next(1, 4);
            var anonShrBQty = rng.Next(1, 4);
            var authShrAQty = rng.Next(1, 4);
            var authShrBQty = rng.Next(1, 4);
            var anonOnlyQty = rng.Next(1, 4);
            var authOnlyQty = rng.Next(1, 4);

            // 1. Anon cart — capture the token emitted by the first AddLine response.
            var anonClient = factory.CreateClient();
            var firstResp = await anonClient.PostAsJsonAsync("/v1/customer/cart/lines",
                new { marketCode = "ksa", productId = sharedA, qty = anonShrAQty });
            firstResp.StatusCode.Should().Be(HttpStatusCode.OK, because: $"scenario {scenario}: {await firstResp.Content.ReadAsStringAsync()}");
            var cartToken = firstResp.Headers.GetValues("X-Cart-Token").Single();
            await AddLine(anonClient, "ksa", sharedB, anonShrBQty);
            await AddLine(anonClient, "ksa", anonOnly, anonOnlyQty);

            // 2. Auth cart.
            var (accessToken, _) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
            var authClient = factory.CreateClient();
            CartCustomerAuthHelper.SetBearer(authClient, accessToken);
            await AddLine(authClient, "ksa", sharedA, authShrAQty);
            await AddLine(authClient, "ksa", sharedB, authShrBQty);
            await AddLine(authClient, "ksa", authOnly, authOnlyQty);

            // 3. Merge.
            var mergeClient = factory.CreateClient();
            CartCustomerAuthHelper.SetBearer(mergeClient, accessToken);
            mergeClient.DefaultRequestHeaders.Add("X-Cart-Token", cartToken);
            var mergeResp = await mergeClient.PostAsJsonAsync("/v1/customer/cart/merge", new { marketCode = "ksa" });
            var body = await mergeResp.Content.ReadAsStringAsync();
            mergeResp.StatusCode.Should().Be(HttpStatusCode.OK, because: $"scenario {scenario}: {body}");

            var payload = JsonDocument.Parse(body).RootElement;
            var lines = payload.GetProperty("lines");

            lines.GetArrayLength().Should().Be(4, because: $"scenario {scenario}: body={body}");

            QtyFor(lines, sharedA).Should().Be(anonShrAQty + authShrAQty, because: $"scenario {scenario} sharedA");
            QtyFor(lines, sharedB).Should().Be(anonShrBQty + authShrBQty, because: $"scenario {scenario} sharedB");
            QtyFor(lines, anonOnly).Should().Be(anonOnlyQty, because: $"scenario {scenario} anonOnly");
            QtyFor(lines, authOnly).Should().Be(authOnlyQty, because: $"scenario {scenario} authOnly");

            // Exactly one active cart after merge — the auth cart.
            await using var assertScope = factory.Services.CreateAsyncScope();
            var db = assertScope.ServiceProvider.GetRequiredService<CartDbContext>();
            var activeCarts = await db.Carts.AsNoTracking().Where(c => c.Status == "active").ToListAsync();
            activeCarts.Should().ContainSingle(because: $"scenario {scenario}: one active cart expected");
            activeCarts[0].AccountId.Should().NotBeNull();
        }
    }

    private static async Task<Guid> MakeProduct(IServiceProvider sp, Guid warehouseId, string sku)
    {
        var id = await CartTestSeedHelper.CreatePublishedProductAsync(sp, sku, ["ksa"]);
        await CartTestSeedHelper.UpsertStockAsync(sp, id, warehouseId, onHand: 50);
        await CartTestSeedHelper.AddBatchAsync(sp, id, warehouseId, $"LOT-{sku}",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 50);
        return id;
    }

    private static async Task AddLine(HttpClient client, string market, Guid productId, int qty)
    {
        var resp = await client.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = market, productId, qty });
        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, because: body);
    }

    private static int QtyFor(JsonElement lines, Guid productId)
    {
        foreach (var l in lines.EnumerateArray())
        {
            if (Guid.Parse(l.GetProperty("productId").GetString()!) == productId)
            {
                return l.GetProperty("qty").GetInt32();
            }
        }
        return -1;
    }
}
