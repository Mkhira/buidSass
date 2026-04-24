using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Cart.Persistence;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Contract.Customer;

/// <summary>
/// C8: CartResolver MUST NOT silently claim an anon cart for an authenticated user on plain
/// add/get paths — the merge must go through POST /merge explicitly so qty summing + notices
/// fire. This test verifies that an authenticated AddLine with a dangling anon cookie creates
/// a NEW auth cart rather than silently adopting the anon one.
/// </summary>
[Collection("cart-fixture")]
public sealed class ResolverContractTests(CartTestFactory factory)
{
    [Fact]
    public async Task AuthenticatedAddLine_WithAnonToken_DoesNotSilentlyClaim()
    {
        await factory.ResetDatabaseAsync();
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-RESOLVE", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-res", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 20);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-RES",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 20);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var anon = factory.CreateClient();
        var anonResp = await anon.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 2 });
        anonResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cartToken = anonResp.Headers.GetValues("X-Cart-Token").Single();

        var (accessToken, accountId) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var authClient = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(authClient, accessToken);
        authClient.DefaultRequestHeaders.Add("X-Cart-Token", cartToken);

        var authAddResp = await authClient.PostAsJsonAsync("/v1/customer/cart/lines",
            new { marketCode = "ksa", productId, qty = 1 });
        authAddResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // DB state: anon cart UNTOUCHED (still anon, still has qty=2); auth cart holds qty=1.
        await using var assertScope = factory.Services.CreateAsyncScope();
        var cartDb = assertScope.ServiceProvider.GetRequiredService<CartDbContext>();

        var anonCart = await cartDb.Carts.AsNoTracking()
            .SingleAsync(c => c.AccountId == null && c.CartTokenHash != null && c.Status == "active");
        anonCart.AccountId.Should().BeNull(because: "anon cart must NOT be silently claimed on AddLine (C8)");
        var anonLines = await cartDb.CartLines.AsNoTracking().Where(l => l.CartId == anonCart.Id).ToListAsync();
        anonLines.Should().ContainSingle();
        anonLines[0].Qty.Should().Be(2);

        var authCart = await cartDb.Carts.AsNoTracking()
            .SingleAsync(c => c.AccountId == accountId && c.Status == "active");
        var authLines = await cartDb.CartLines.AsNoTracking().Where(l => l.CartId == authCart.Id).ToListAsync();
        authLines.Should().ContainSingle();
        authLines[0].Qty.Should().Be(1);
    }
}
