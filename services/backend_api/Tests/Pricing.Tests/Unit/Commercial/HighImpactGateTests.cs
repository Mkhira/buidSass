using BackendApi.Modules.Pricing.Primitives.Commercial;
using FluentAssertions;

namespace Pricing.Tests.Unit.Commercial;

public sealed class HighImpactGateTests
{
    private static CommercialThresholdPolicy DefaultPolicy(bool gateEnabled = true) => new(
        MarketCode: "SA",
        GateEnabled: gateEnabled,
        ThresholdPercentOff: 30m,
        ThresholdAmountOffMinor: 5_000_000L,
        ThresholdDurationDays: 14,
        CouponInFlightGraceSeconds: 1800,
        PromotionInFlightGraceSeconds: 1800);

    [Fact]
    public void Triggers_When_PercentOff_Above_Threshold()
    {
        var candidate = new HighImpactCandidate(
            PercentOff: 50m,
            AmountOffMinor: null,
            CapMinor: null,
            PerCustomerLimit: 1,
            OverallLimit: 100,
            ValidFrom: DateTimeOffset.UtcNow,
            ValidTo: DateTimeOffset.UtcNow.AddDays(7));

        HighImpactGate.IsTriggered(candidate, DefaultPolicy()).Should().BeTrue();
    }

    [Fact]
    public void Triggers_When_AmountOff_Above_Threshold()
    {
        var candidate = new HighImpactCandidate(
            PercentOff: null,
            AmountOffMinor: 6_000_000L,
            CapMinor: null,
            PerCustomerLimit: 1,
            OverallLimit: 100,
            ValidFrom: DateTimeOffset.UtcNow,
            ValidTo: DateTimeOffset.UtcNow.AddDays(7));

        HighImpactGate.IsTriggered(candidate, DefaultPolicy()).Should().BeTrue();
    }

    [Fact]
    public void Triggers_When_BothLimits_Unset()
    {
        var candidate = new HighImpactCandidate(
            PercentOff: 5m,
            AmountOffMinor: null,
            CapMinor: null,
            PerCustomerLimit: null,
            OverallLimit: null,
            ValidFrom: DateTimeOffset.UtcNow,
            ValidTo: DateTimeOffset.UtcNow.AddDays(7));

        HighImpactGate.IsTriggered(candidate, DefaultPolicy()).Should().BeTrue();
    }

    [Fact]
    public void Triggers_When_DurationDays_Above_Threshold()
    {
        var from = DateTimeOffset.UtcNow;
        var candidate = new HighImpactCandidate(
            PercentOff: 5m,
            AmountOffMinor: null,
            CapMinor: null,
            PerCustomerLimit: 1,
            OverallLimit: 100,
            ValidFrom: from,
            ValidTo: from.AddDays(30));

        HighImpactGate.IsTriggered(candidate, DefaultPolicy()).Should().BeTrue();
    }

    [Fact]
    public void DoesNotTrigger_When_NoCriteriaMet()
    {
        var from = DateTimeOffset.UtcNow;
        var candidate = new HighImpactCandidate(
            PercentOff: 5m,
            AmountOffMinor: 10_000L,
            CapMinor: null,
            PerCustomerLimit: 1,
            OverallLimit: 100,
            ValidFrom: from,
            ValidTo: from.AddDays(7));

        HighImpactGate.IsTriggered(candidate, DefaultPolicy()).Should().BeFalse();
    }

    [Fact]
    public void GateDisabled_ShortCircuits_AlwaysFalse()
    {
        var candidate = new HighImpactCandidate(
            PercentOff: 99m,
            AmountOffMinor: 999_000_000L,
            CapMinor: null,
            PerCustomerLimit: null,
            OverallLimit: null,
            ValidFrom: DateTimeOffset.UtcNow,
            ValidTo: DateTimeOffset.UtcNow.AddDays(365));

        HighImpactGate.IsTriggered(candidate, DefaultPolicy(gateEnabled: false)).Should().BeFalse();
    }

    [Fact]
    public void NullThresholdPercent_DisablesOnlyPercentCriterion()
    {
        var policy = DefaultPolicy() with { ThresholdPercentOff = null };

        // Despite a 99% percent off, the percent criterion is disabled.
        var fromCandidate = new HighImpactCandidate(
            PercentOff: 99m,
            AmountOffMinor: 1_000L,
            CapMinor: null,
            PerCustomerLimit: 1,
            OverallLimit: 100,
            ValidFrom: DateTimeOffset.UtcNow,
            ValidTo: DateTimeOffset.UtcNow.AddDays(7));

        HighImpactGate.IsTriggered(fromCandidate, policy).Should().BeFalse();
    }
}
