using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration;

[Collection("pricing-fixture")]
public sealed class PromotionStackTests(PricingTestFactory factory)
{
    [Fact]
    public async Task BogoThenCoupon_StackCorrectly()
    {
        await factory.ResetDatabaseAsync();
        Guid productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await PricingTestSeedHelper.SeedKsaVatAsync(scope.ServiceProvider);
            productId = await PricingTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider, "STACK-001", priceHintMinor: 10_000, marketCodes: new[] { "ksa" });

            await PricingTestSeedHelper.CreatePromotionAsync(
                scope.ServiceProvider, "bogo",
                new
                {
                    qualifyingProductId = productId.ToString(),
                    rewardProductId = productId.ToString(),
                    qualifyQty = 2,
                    rewardQty = 1,
                    rewardPercentBps = 10_000,
                });

            await PricingTestSeedHelper.CreateCouponAsync(
                scope.ServiceProvider, code: "TEN", kind: "percent", value: 1_000);
        }

        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/v1/customer/pricing/price-cart", new
        {
            marketCode = "ksa",
            locale = "en",
            lines = new[] { new { productId, qty = 3 } },
            couponCode = "TEN",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PriceCartResponseDto>();
        // 3 × 10_000 = 30_000 list; bogo → 20_000; coupon 10% → 18_000; VAT 15% = 2_700; gross = 20_700
        body!.Totals.SubtotalMinor.Should().Be(18_000);
        body.Totals.TaxMinor.Should().Be(2_700);
        body.Totals.GrandTotalMinor.Should().Be(20_700);
    }
}
