using BackendApi.Modules.Pricing.Primitives;
using BackendApi.Modules.Pricing.Primitives.Layers;
using FluentAssertions;

namespace Pricing.Tests.Unit.Layers;

public sealed class PromotionLayerTests
{
    [Fact]
    public void PercentOff_AppliesToAllLines()
    {
        var ctx = NewCtx();
        var pid = Guid.NewGuid();
        var ws = new PricingWorkingSet(ctx, new[]
        {
            new WorkingLine(pid, 1, 10_000, false, Array.Empty<Guid>()) { NetMinor = 10_000 },
        });
        var promo = MakePromo("percent_off", percentBps: 1_000); // 10%
        new PromotionLayer(new[] { promo }).Apply(ws);
        ws.Lines[0].NetMinor.Should().Be(9_000);
        ws.Lines[0].Explanation.Should().ContainSingle(e => e.Layer == "promotion");
    }

    [Fact]
    public void Bogo_ThreeUnits_OneFree()
    {
        var ctx = NewCtx();
        var pid = Guid.NewGuid();
        var ws = new PricingWorkingSet(ctx, new[]
        {
            new WorkingLine(pid, 3, 10_000, false, Array.Empty<Guid>()) { NetMinor = 30_000 },
        });
        var promo = MakeBogo(qualifyingId: pid, qualifyQty: 2, rewardQty: 1, rewardPctBps: 10_000);
        new PromotionLayer(new[] { promo }).Apply(ws);

        // 1 unit of the 3 is free: discount = 10_000 (one unit's net)
        ws.Lines[0].NetMinor.Should().Be(20_000);
    }

    [Fact]
    public void Promotion_Skipped_OutOfMarket()
    {
        var ctx = new PricingContext("eg", "en", null, Array.Empty<PricingContextLine>(), null, null, null, DateTimeOffset.UtcNow, PricingMode.Preview);
        var pid = Guid.NewGuid();
        var ws = new PricingWorkingSet(ctx, new[]
        {
            new WorkingLine(pid, 1, 10_000, false, Array.Empty<Guid>()) { NetMinor = 10_000 },
        });
        var promo = MakePromo("percent_off", percentBps: 1_000, markets: new[] { "ksa" });
        new PromotionLayer(new[] { promo }).Apply(ws);
        ws.Lines[0].NetMinor.Should().Be(10_000);
    }

    private static PromotionSnapshot MakePromo(string kind, int? percentBps = null, long? amount = null, string[]? markets = null) => new(
        Id: Guid.NewGuid(),
        Kind: kind,
        Priority: 0,
        IsActive: true,
        StartsAt: null,
        EndsAt: null,
        MarketCodes: markets ?? new[] { "ksa" },
        AppliesToProductIds: null,
        AppliesToCategoryIds: null,
        PercentBps: percentBps,
        AmountMinor: amount,
        BogoQualifyingProductId: null,
        BogoRewardProductId: null,
        BogoQualifyQty: null,
        BogoRewardQty: null,
        BogoRewardPercentBps: null);

    private static PromotionSnapshot MakeBogo(Guid qualifyingId, int qualifyQty, int rewardQty, int rewardPctBps) => new(
        Id: Guid.NewGuid(),
        Kind: "bogo",
        Priority: 0,
        IsActive: true,
        StartsAt: null,
        EndsAt: null,
        MarketCodes: new[] { "ksa" },
        AppliesToProductIds: null,
        AppliesToCategoryIds: null,
        PercentBps: null,
        AmountMinor: null,
        BogoQualifyingProductId: qualifyingId,
        BogoRewardProductId: qualifyingId,
        BogoQualifyQty: qualifyQty,
        BogoRewardQty: rewardQty,
        BogoRewardPercentBps: rewardPctBps);

    private static PricingContext NewCtx() => new(
        "ksa", "en", null, Array.Empty<PricingContextLine>(), null, null, null, DateTimeOffset.UtcNow, PricingMode.Preview);
}
