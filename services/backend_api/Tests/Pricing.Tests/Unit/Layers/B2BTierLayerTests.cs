using BackendApi.Modules.Pricing.Primitives;
using BackendApi.Modules.Pricing.Primitives.Layers;
using FluentAssertions;

namespace Pricing.Tests.Unit.Layers;

public sealed class B2BTierLayerTests
{
    [Fact]
    public void Apply_OverridesNet_WhenTierPriceExists()
    {
        var pid = Guid.NewGuid();
        var ctx = NewCtx();
        var ws = new PricingWorkingSet(ctx, new[]
        {
            new WorkingLine(pid, 2, 10_000, false, Array.Empty<Guid>()) { NetMinor = 20_000 },
        });

        var layer = new B2BTierLayer(
            tierPricesByProductId: new Dictionary<Guid, long> { [pid] = 9_000 },
            tierSlug: "tier-2");
        layer.Apply(ws);

        ws.Lines[0].NetMinor.Should().Be(18_000);
        ws.Lines[0].Explanation.Should().ContainSingle(e => e.Layer == "tier" && e.AppliedMinor == -2_000);
    }

    [Fact]
    public void Apply_NoOp_WhenNoTierSlug()
    {
        var pid = Guid.NewGuid();
        var ctx = NewCtx();
        var ws = new PricingWorkingSet(ctx, new[]
        {
            new WorkingLine(pid, 1, 10_000, false, Array.Empty<Guid>()) { NetMinor = 10_000 },
        });
        var layer = new B2BTierLayer(new Dictionary<Guid, long> { [pid] = 9_000 }, tierSlug: null);
        layer.Apply(ws);
        ws.Lines[0].NetMinor.Should().Be(10_000);
        ws.Lines[0].Explanation.Should().BeEmpty();
    }

    private static PricingContext NewCtx() => new(
        "ksa", "en", null, Array.Empty<PricingContextLine>(), null, null, null, DateTimeOffset.UtcNow, PricingMode.Preview);
}
