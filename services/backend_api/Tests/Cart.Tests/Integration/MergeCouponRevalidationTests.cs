using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Integration;

/// <summary>
/// Spec 009 edge case 5: "Anonymous cart merge where anon had a coupon → coupon preserved if
/// still valid for the authenticated account." If the carry-over would now be rejected by the
/// coupon gates (expired / limit reached / restricted / market mismatch), merge MUST drop it
/// and surface a merge notice — not silently copy the stale code onto the auth cart.
/// </summary>
[Collection("cart-fixture")]
public sealed class MergeCouponRevalidationTests(CartTestFactory factory)
{
    [Fact]
    public async Task Merge_AnonCouponExpired_DropsCouponWithNotice()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-COUP-MRG", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-cmg", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 20);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-CMG",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 20);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        // Seed the coupon ACTIVE so ApplyCoupon passes on the anon cart.
        var pricingDb = seedScope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var coupon = new Coupon
        {
            Id = Guid.NewGuid(),
            Code = "MRGCPN",
            Kind = "percent",
            Value = 1000,
            MarketCodes = new[] { "ksa" },
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        pricingDb.Coupons.Add(coupon);
        await pricingDb.SaveChangesAsync();

        // Anon cart + add line + apply coupon.
        var anonClient = factory.CreateClient();
        var addResp = await anonClient.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 1 });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = addResp.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("cart_token=", StringComparison.Ordinal))
            .Split(';')[0];
        var cartToken = cookie["cart_token=".Length..];

        using var applyReq = new HttpRequestMessage(HttpMethod.Post, "/v1/customer/cart/coupon");
        applyReq.Headers.Add("Cookie", cookie);
        applyReq.Content = JsonContent.Create(new { marketCode = "ksa", code = "MRGCPN" });
        (await anonClient.SendAsync(applyReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Invalidate the coupon between anon-apply and merge — e.g. admin deactivates it.
        await using (var mutateScope = factory.Services.CreateAsyncScope())
        {
            var pd = mutateScope.ServiceProvider.GetRequiredService<PricingDbContext>();
            await pd.Coupons.Where(c => c.Code == "MRGCPN")
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
        }

        var (accessToken, _) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var mergeClient = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(mergeClient, accessToken);
        mergeClient.DefaultRequestHeaders.Add("X-Cart-Token", cartToken);

        var mergeResp = await mergeClient.PostAsJsonAsync("/v1/customer/cart/merge", new { marketCode = "ksa" });
        mergeResp.StatusCode.Should().Be(HttpStatusCode.OK, because: await mergeResp.Content.ReadAsStringAsync());
        var payload = await mergeResp.Content.ReadFromJsonAsync<JsonElement>();

        // Coupon should NOT have been adopted onto the auth cart.
        (payload.TryGetProperty("couponCode", out var couponEl) && couponEl.ValueKind != JsonValueKind.Null
            ? couponEl.GetString() : null).Should().BeNull();

        // Notices should surface the drop reason.
        var notices = payload.GetProperty("mergeNotices");
        notices.GetArrayLength().Should().BeGreaterThan(0);
        notices.EnumerateArray().Any(n =>
            n.GetProperty("reasonCode").GetString() == "cart.coupon.invalid").Should().BeTrue();

        // DB state — the auth cart's CouponCode column is null.
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var cartDb = verifyScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var authCart = await cartDb.Carts.AsNoTracking()
            .SingleAsync(c => c.MarketCode == "ksa" && c.Status == CartStatuses.Active && c.AccountId != null);
        authCart.CouponCode.Should().BeNull();
    }

    [Fact]
    public async Task Merge_AnonCouponStillValid_AdoptsIt()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-CMG-OK", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-cmg-ok", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 20);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-CMG-OK",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 20);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var pricingDb = seedScope.ServiceProvider.GetRequiredService<PricingDbContext>();
        pricingDb.Coupons.Add(new Coupon
        {
            Id = Guid.NewGuid(),
            Code = "KEEPME",
            Kind = "percent",
            Value = 500,
            MarketCodes = new[] { "ksa" },
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await pricingDb.SaveChangesAsync();

        var anonClient = factory.CreateClient();
        var addResp = await anonClient.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 1 });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = addResp.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("cart_token=", StringComparison.Ordinal))
            .Split(';')[0];
        var cartToken = cookie["cart_token=".Length..];

        using var applyReq = new HttpRequestMessage(HttpMethod.Post, "/v1/customer/cart/coupon");
        applyReq.Headers.Add("Cookie", cookie);
        applyReq.Content = JsonContent.Create(new { marketCode = "ksa", code = "KEEPME" });
        (await anonClient.SendAsync(applyReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        var (accessToken, _) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var mergeClient = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(mergeClient, accessToken);
        mergeClient.DefaultRequestHeaders.Add("X-Cart-Token", cartToken);

        var mergeResp = await mergeClient.PostAsJsonAsync("/v1/customer/cart/merge", new { marketCode = "ksa" });
        mergeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await mergeResp.Content.ReadFromJsonAsync<JsonElement>();

        payload.GetProperty("couponCode").GetString().Should().Be("KEEPME");
    }
}
