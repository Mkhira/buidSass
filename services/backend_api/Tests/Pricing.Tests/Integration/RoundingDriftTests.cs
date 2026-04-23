using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration;

[Collection("pricing-fixture")]
public sealed class RoundingDriftTests(PricingTestFactory factory)
{
    [Fact]
    public async Task TwentyLineCart_ZeroDrift()
    {
        await factory.ResetDatabaseAsync();
        var productIds = new List<Guid>();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await PricingTestSeedHelper.SeedKsaVatAsync(scope.ServiceProvider);
            for (var i = 0; i < 20; i++)
            {
                var pid = await PricingTestSeedHelper.CreatePublishedProductAsync(
                    scope.ServiceProvider, sku: $"DRIFT-{i:D3}", priceHintMinor: 333 + i * 7, marketCodes: new[] { "ksa" });
                productIds.Add(pid);
            }
            await PricingTestSeedHelper.CreateCouponAsync(
                scope.ServiceProvider, code: "DRIFT7", kind: "percent", value: 733);
        }

        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/v1/customer/pricing/price-cart", new
        {
            marketCode = "ksa",
            locale = "en",
            lines = productIds.Select(p => new { productId = p, qty = 3 }).ToArray(),
            couponCode = "DRIFT7",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PriceCartResponseDto>();
        body.Should().NotBeNull();

        var sumOfGross = body!.Lines.Sum(l => l.GrossMinor);
        var sumOfNetPlusTax = body.Lines.Sum(l => l.NetMinor + l.TaxMinor);

        sumOfGross.Should().Be(body.Totals.GrandTotalMinor);
        sumOfNetPlusTax.Should().Be(body.Totals.GrandTotalMinor);
    }
}
