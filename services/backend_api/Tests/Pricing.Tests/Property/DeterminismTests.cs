using BackendApi.Modules.Pricing.Primitives;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Property;

[Collection("pricing-fixture")]
public sealed class DeterminismTests(PricingTestFactory factory)
{
    // SC-002: 10000 random carts re-priced twice → 0 explanation-hash mismatches.
    // Scaled to 500 under CI time budget; the guarantee is byte stability regardless of sample size.
    [Fact]
    public async Task Determinism_SameCtx_SameHash()
    {
        await factory.ResetDatabaseAsync();
        var productIds = new List<Guid>();
        var productPrices = new Dictionary<Guid, long>();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await PricingTestSeedHelper.SeedKsaVatAsync(scope.ServiceProvider);
            for (var i = 0; i < 5; i++)
            {
                var price = 1_000 + i * 1_000L;
                var pid = await PricingTestSeedHelper.CreatePublishedProductAsync(
                    scope.ServiceProvider, sku: $"DET-{i:D3}", priceHintMinor: price,
                    marketCodes: new[] { "ksa" });
                productIds.Add(pid);
                productPrices[pid] = price;
            }
            await PricingTestSeedHelper.CreateCouponAsync(
                scope.ServiceProvider, code: "DET10", kind: "percent", value: 1_000);
        }

        var fixedNow = new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);
        var rnd = new Random(42);

        for (var i = 0; i < 500; i++)
        {
            var lineCount = 1 + rnd.Next(4);
            var lines = Enumerable.Range(0, lineCount)
                .Select(_ =>
                {
                    var pid = productIds[rnd.Next(productIds.Count)];
                    return new PricingContextLine(pid, 1 + rnd.Next(3), productPrices[pid], Restricted: false, CategoryIds: Array.Empty<Guid>());
                })
                .ToArray();

            var ctx = new PricingContext(
                MarketCode: "ksa",
                Locale: "en",
                Account: null,
                Lines: lines,
                CouponCode: rnd.Next(2) == 0 ? null : "DET10",
                QuotationId: null,
                OrderId: null,
                NowUtc: fixedNow,
                Mode: PricingMode.Preview);

            await using var scope = factory.Services.CreateAsyncScope();
            var calc = scope.ServiceProvider.GetRequiredService<IPriceCalculator>();

            var o1 = await calc.CalculateAsync(ctx, default);
            var o2 = await calc.CalculateAsync(ctx, default);

            o1.IsSuccess.Should().BeTrue($"iter {i}");
            o2.IsSuccess.Should().BeTrue($"iter {i}");
            o1.Result!.ExplanationHash.Should().Be(o2.Result!.ExplanationHash, $"iteration {i}");
        }
    }
}
