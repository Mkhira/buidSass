using BackendApi.Modules.Checkout.Primitives;
using FluentAssertions;

namespace Checkout.Tests.Unit;

public sealed class DriftDetectorTests
{
    private static readonly DriftDetector Detector = new();

    private static DriftDetector.PricingSnapshot Snap(
        long sub = 10_000, long disc = 0, long tax = 1_500, long grand = 11_500,
        string currency = "SAR", string? coupon = null,
        params (Guid productId, int qty, long netMinor, long grossMinor)[] lines)
    {
        if (lines.Length == 0)
        {
            lines = new[] { (Guid.Parse("00000000-0000-0000-0000-000000000001"), 1, 10_000L, 11_500L) };
        }
        return new DriftDetector.PricingSnapshot(
            sub, disc, tax, grand, currency, coupon,
            lines.Select(l => new DriftDetector.LineSnapshot(l.productId, l.qty, l.netMinor, l.grossMinor)).ToArray());
    }

    [Fact]
    public void Hash_IsDeterministic_ForSameInputs()
    {
        var a = Detector.Hash(Snap());
        var b = Detector.Hash(Snap());
        a.Should().BeEquivalentTo(b);
    }

    [Fact]
    public void Hash_IsOrderIndependent_ForLines()
    {
        var p1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var p2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var aHash = Detector.Hash(Snap(lines: new[] { (p1, 1, 1_000L, 1_150L), (p2, 2, 2_000L, 2_300L) }));
        var bHash = Detector.Hash(Snap(lines: new[] { (p2, 2, 2_000L, 2_300L), (p1, 1, 1_000L, 1_150L) }));
        aHash.Should().BeEquivalentTo(bHash,
            because: "canonicalising by productId makes line order irrelevant");
    }

    [Fact]
    public void HasDrifted_TrueWhenTotalsChange()
    {
        var before = Detector.Hash(Snap(grand: 11_500));
        var after = Detector.Hash(Snap(grand: 12_000));
        Detector.HasDrifted(before, after).Should().BeTrue();
    }

    [Fact]
    public void HasDrifted_TrueWhenCouponAppearsOrExpires()
    {
        var withCoupon = Detector.Hash(Snap(coupon: "WELCOME10"));
        var withoutCoupon = Detector.Hash(Snap());
        Detector.HasDrifted(withCoupon, withoutCoupon).Should().BeTrue();
    }

    [Fact]
    public void HasDrifted_FalseForIdenticalEconomicOutput()
    {
        var snap = Snap();
        var a = Detector.Hash(snap);
        var b = Detector.Hash(snap);
        Detector.HasDrifted(a, b).Should().BeFalse();
    }

    [Fact]
    public void HasDrifted_NullPrevious_ReturnsFalse()
    {
        // First-time callers don't have a previous hash yet — that's not a drift event.
        Detector.HasDrifted(null, Detector.Hash(Snap())).Should().BeFalse();
    }
}
