using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Contract.Customer;

[Collection("pricing-fixture")]
public sealed class BogoContractTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Bogo_ThreeQualifying_OneFree()
    {
        await factory.ResetDatabaseAsync();
        Guid productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await PricingTestSeedHelper.SeedKsaVatAsync(scope.ServiceProvider);
            productId = await PricingTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider, "BOGO-001", priceHintMinor: 10_000, marketCodes: new[] { "ksa" });

            await PricingTestSeedHelper.CreatePromotionAsync(
                scope.ServiceProvider,
                kind: "bogo",
                config: new
                {
                    qualifyingProductId = productId.ToString(),
                    rewardProductId = productId.ToString(),
                    qualifyQty = 2,
                    rewardQty = 1,
                    rewardPercentBps = 10_000,
                });
        }

        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/v1/customer/pricing/price-cart", new
        {
            marketCode = "ksa",
            locale = "en",
            lines = new[] { new { productId, qty = 3 } },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PriceCartResponseDto>();
        // 3 × 10_000 = 30_000 list; bogo gives 1 free → net 20_000; VAT 15% = 3_000; gross 23_000
        body!.Totals.SubtotalMinor.Should().Be(20_000);
        body.Totals.TaxMinor.Should().Be(3_000);
        body.Totals.GrandTotalMinor.Should().Be(23_000);
        body.Lines[0].Layers.Should().Contain(l => l.Layer == "promotion" && l.RuleKind == "bogo");
    }
}
