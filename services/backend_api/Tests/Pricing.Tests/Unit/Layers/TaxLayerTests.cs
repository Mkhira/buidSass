using BackendApi.Modules.Pricing.Primitives;
using BackendApi.Modules.Pricing.Primitives.Layers;
using FluentAssertions;

namespace Pricing.Tests.Unit.Layers;

public sealed class TaxLayerTests
{
    [Fact]
    public void Apply_AddsVatOnTop()
    {
        var ctx = NewCtx();
        var pid = Guid.NewGuid();
        var ws = new PricingWorkingSet(ctx, new[]
        {
            new WorkingLine(pid, 1, 10_000, false, Array.Empty<Guid>()) { NetMinor = 10_000 },
        });
        ws.TaxRate = new TaxRateSnapshot(Guid.NewGuid(), "ksa", "vat", RateBps: 1_500); // 15%
        new TaxLayer().Apply(ws);
        ws.Lines[0].TaxMinor.Should().Be(1_500);
    }

    [Fact]
    public void Apply_Throws_WhenNoTaxRate()
    {
        var ctx = NewCtx();
        var ws = new PricingWorkingSet(ctx, new[]
        {
            new WorkingLine(Guid.NewGuid(), 1, 10_000, false, Array.Empty<Guid>()) { NetMinor = 10_000 },
        });
        var act = () => new TaxLayer().Apply(ws);
        act.Should().Throw<InvalidOperationException>();
    }

    private static PricingContext NewCtx() => new(
        "ksa", "en", null, Array.Empty<PricingContextLine>(), null, null, null, DateTimeOffset.UtcNow, PricingMode.Preview);
}
