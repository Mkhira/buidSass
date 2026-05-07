using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using FluentAssertions;

namespace Pricing.Tests.Unit.Commercial;

/// <summary>
/// Spec 007-b T045 — verifies that <see cref="CommercialThresholdPolicy"/>
/// loaded from a fake <see cref="CommercialThreshold"/> row honors the
/// null-criterion-disables-only-that-criterion semantic from FR-025.
///
/// The four FR-025 criteria are: percent-off, amount-off (or cap), usage-limit
/// (both unset), and schedule duration. Each criterion's threshold field on the
/// row may be null — that disables ONLY that criterion, not the entire gate.
/// Disabling the entire gate requires <c>GateEnabled=false</c> instead.
/// </summary>
public sealed class CommercialThresholdPolicyTests
{
    private static CommercialThreshold BaseRow(string market = "SA") => new()
    {
        MarketCode = market,
        GateEnabled = true,
        ThresholdPercentOff = 30m,
        ThresholdAmountOffMinor = 5_000_000L,
        ThresholdDurationDays = 14,
        CouponInFlightGraceSeconds = 1800,
        PromotionInFlightGraceSeconds = 1800,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedByActorId = Guid.NewGuid(),
    };

    private static CommercialThresholdPolicy ToPolicy(CommercialThreshold row) => new(
        MarketCode: row.MarketCode,
        GateEnabled: row.GateEnabled,
        ThresholdPercentOff: row.ThresholdPercentOff,
        ThresholdAmountOffMinor: row.ThresholdAmountOffMinor,
        ThresholdDurationDays: row.ThresholdDurationDays,
        CouponInFlightGraceSeconds: row.CouponInFlightGraceSeconds,
        PromotionInFlightGraceSeconds: row.PromotionInFlightGraceSeconds);

    private static HighImpactCandidate Candidate(
        decimal? percentOff = null,
        long? amountOffMinor = null,
        long? capMinor = null,
        int? perCustomerLimit = 5,
        int? overallLimit = 100,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validTo = null) => new(
            PercentOff: percentOff,
            AmountOffMinor: amountOffMinor,
            CapMinor: capMinor,
            PerCustomerLimit: perCustomerLimit,
            OverallLimit: overallLimit,
            ValidFrom: validFrom ?? DateTimeOffset.UtcNow,
            ValidTo: validTo ?? DateTimeOffset.UtcNow.AddDays(7));

    [Fact]
    public void Loads_All_Fields_From_Threshold_Row()
    {
        var row = BaseRow("EG");
        row.GateEnabled = true;
        row.ThresholdAmountOffMinor = 25_000_000L;

        var policy = ToPolicy(row);

        policy.MarketCode.Should().Be("EG");
        policy.GateEnabled.Should().BeTrue();
        policy.ThresholdPercentOff.Should().Be(30m);
        policy.ThresholdAmountOffMinor.Should().Be(25_000_000L);
        policy.ThresholdDurationDays.Should().Be(14);
        policy.CouponInFlightGraceSeconds.Should().Be(1800);
        policy.PromotionInFlightGraceSeconds.Should().Be(1800);
    }

    [Fact]
    public void Null_PercentOff_Disables_Only_Percent_Criterion()
    {
        var row = BaseRow();
        row.ThresholdPercentOff = null;
        var policy = ToPolicy(row);

        // Percent criterion alone cannot fire (null threshold).
        var percentCandidate = Candidate(percentOff: 99m, amountOffMinor: null);
        HighImpactGate.IsTriggered(percentCandidate, policy).Should().BeFalse();

        // Amount criterion still fires (threshold preserved).
        var amountCandidate = Candidate(percentOff: null, amountOffMinor: 6_000_000L);
        HighImpactGate.IsTriggered(amountCandidate, policy).Should().BeTrue();
    }

    [Fact]
    public void Null_AmountOff_Disables_Only_Amount_Criterion()
    {
        var row = BaseRow();
        row.ThresholdAmountOffMinor = null;
        var policy = ToPolicy(row);

        var amountCandidate = Candidate(amountOffMinor: 999_999_999L);
        HighImpactGate.IsTriggered(amountCandidate, policy).Should().BeFalse();

        var percentCandidate = Candidate(percentOff: 50m);
        HighImpactGate.IsTriggered(percentCandidate, policy).Should().BeTrue();
    }

    [Fact]
    public void Cap_Path_Triggers_Amount_Criterion_When_AmountOff_Is_Null()
    {
        // Criterion 2 evaluates `cap_minor ?? amount_off_minor` against the
        // configured threshold. Lock down the "or cap" branch with a candidate
        // that carries no amount_off but a cap above threshold (CodeRabbit nit).
        var policy = ToPolicy(BaseRow());

        var capCandidate = Candidate(percentOff: null, amountOffMinor: null, capMinor: 6_000_000L);
        HighImpactGate.IsTriggered(capCandidate, policy).Should().BeTrue();
    }

    [Fact]
    public void Null_DurationDays_Disables_Only_Duration_Criterion()
    {
        var row = BaseRow();
        row.ThresholdDurationDays = null;
        var policy = ToPolicy(row);

        // Duration criterion alone cannot fire.
        var longRunCandidate = Candidate(
            percentOff: null,
            amountOffMinor: null,
            validFrom: DateTimeOffset.UtcNow,
            validTo: DateTimeOffset.UtcNow.AddDays(365));
        HighImpactGate.IsTriggered(longRunCandidate, policy).Should().BeFalse();

        // Percent criterion still fires.
        var percentCandidate = Candidate(percentOff: 50m);
        HighImpactGate.IsTriggered(percentCandidate, policy).Should().BeTrue();
    }

    [Fact]
    public void Both_UsageLimits_Null_Always_Triggers_Regardless_Of_Other_Nulls()
    {
        // Criterion 3 is independent of policy threshold fields — it fires whenever
        // both per-customer and overall usage limits on the candidate are null.
        var row = BaseRow();
        row.ThresholdPercentOff = null;
        row.ThresholdAmountOffMinor = null;
        row.ThresholdDurationDays = null;
        var policy = ToPolicy(row);

        var unlimitedCandidate = Candidate(perCustomerLimit: null, overallLimit: null);
        HighImpactGate.IsTriggered(unlimitedCandidate, policy).Should().BeTrue();
    }

    [Fact]
    public void GateEnabled_False_ShortCircuits_Even_With_Configured_Thresholds()
    {
        var row = BaseRow();
        row.GateEnabled = false;
        var policy = ToPolicy(row);

        var hugeCandidate = Candidate(
            percentOff: 99m,
            amountOffMinor: 999_999_999L,
            perCustomerLimit: null,
            overallLimit: null,
            validTo: DateTimeOffset.UtcNow.AddYears(10));

        HighImpactGate.IsTriggered(hugeCandidate, policy).Should().BeFalse();
    }
}
