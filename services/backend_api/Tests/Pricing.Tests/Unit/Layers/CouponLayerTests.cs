using BackendApi.Modules.Pricing.Primitives;
using BackendApi.Modules.Pricing.Primitives.Layers;
using FluentAssertions;

namespace Pricing.Tests.Unit.Layers;

public sealed class CouponLayerTests
{
    [Fact]
    public void Percent_WithCap_Clamps()
    {
        var ctx = NewCtx();
        var pid = Guid.NewGuid();
        var ws = new PricingWorkingSet(ctx, new[]
        {
            new WorkingLine(pid, 1, 100_000, false, Array.Empty<Guid>()) { NetMinor = 100_000 },
        });
        ws.AppliedCoupon = new AppliedCouponInfo(Guid.NewGuid(), "X", "percent", Value: 1_000 /*10%*/, CapMinor: 5_000, ExcludesRestricted: false);

        new CouponLayer().Apply(ws);
        ws.Lines[0].NetMinor.Should().Be(95_000);
    }

    [Fact]
    public void ExcludesRestricted_SkipsRestrictedLine()
    {
        var ctx = NewCtx();
        var openId = Guid.NewGuid();
        var restrictedId = Guid.NewGuid();
        var ws = new PricingWorkingSet(ctx, new[]
        {
            new WorkingLine(openId, 1, 10_000, false, Array.Empty<Guid>()) { NetMinor = 10_000 },
            new WorkingLine(restrictedId, 1, 10_000, true, Array.Empty<Guid>()) { NetMinor = 10_000 },
        });
        ws.AppliedCoupon = new AppliedCouponInfo(Guid.NewGuid(), "X", "percent", Value: 1_000, CapMinor: null, ExcludesRestricted: true);

        new CouponLayer().Apply(ws);
        ws.Lines[0].NetMinor.Should().Be(9_000);       // discounted
        ws.Lines[1].NetMinor.Should().Be(10_000);      // untouched
    }

    [Fact]
    public void Amount_ClampedToSubtotal()
    {
        var ctx = NewCtx();
        var pid = Guid.NewGuid();
        var ws = new PricingWorkingSet(ctx, new[]
        {
            new WorkingLine(pid, 1, 5_000, false, Array.Empty<Guid>()) { NetMinor = 5_000 },
        });
        ws.AppliedCoupon = new AppliedCouponInfo(Guid.NewGuid(), "X", "amount", Value: 20_000, CapMinor: null, ExcludesRestricted: false);

        new CouponLayer().Apply(ws);
        ws.Lines[0].NetMinor.Should().Be(0);
    }

    private static PricingContext NewCtx() => new(
        "ksa", "en", null, Array.Empty<PricingContextLine>(), null, null, null, DateTimeOffset.UtcNow, PricingMode.Preview);
}
