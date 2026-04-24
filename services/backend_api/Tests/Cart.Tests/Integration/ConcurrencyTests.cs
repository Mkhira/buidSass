using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Cart.Persistence;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Integration;

/// <summary>
/// C4/C5/FR-012: concurrent cart operations must either succeed or return 409, never 500. The
/// retry loop in AddLine absorbs one collision; beyond that, 409 cart.concurrency_conflict is
/// the contract.
/// </summary>
[Collection("cart-fixture")]
public sealed class ConcurrencyTests(CartTestFactory factory)
{
    [Fact]
    public async Task ConcurrentAddLine_SameCart_SameProduct_NoFiveHundred()
    {
        await factory.ResetDatabaseAsync();
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-CC-1", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-cc", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 200);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-CC",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 200);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        // Pre-warm cart + share the cookie across both concurrent clients so they target the
        // same cart row.
        var warmup = factory.CreateClient();
        var firstResp = await warmup.PostAsJsonAsync("/v1/customer/cart/lines",
            new { marketCode = "ksa", productId, qty = 1 });
        firstResp.EnsureSuccessStatusCode();
        var cartToken = firstResp.Headers.GetValues("X-Cart-Token").Single();

        HttpClient Make()
        {
            var c = factory.CreateClient();
            c.DefaultRequestHeaders.Add("X-Cart-Token", cartToken);
            return c;
        }

        // 10 concurrent add-line calls (same cart, same product).
        var tasks = Enumerable.Range(0, 10).Select(_ => Make().PostAsJsonAsync(
            "/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 1 })).ToArray();
        var responses = await Task.WhenAll(tasks);

        foreach (var r in responses)
        {
            // Every response is either 200 OK or 409 concurrency_conflict. Never 500.
            r.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict);
        }
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.OK,
            because: "at least one concurrent request must succeed");
    }
}
