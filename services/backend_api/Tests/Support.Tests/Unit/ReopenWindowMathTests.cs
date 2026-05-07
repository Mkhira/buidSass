using BackendApi.Modules.Support.Primitives;
using FluentAssertions;

namespace Support.Tests.Unit;

/// <summary>
/// Spec 023 T102 · research.md §R-09 — table-driven unit tests covering
/// the pure window + cap math primitive extracted from
/// <c>ReopenTicketHandler</c>. Pure-function tests, no DB / clock fixture.
/// </summary>
public sealed class ReopenWindowMathTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-07T12:00:00+00:00");

    [Fact]
    public void Eligible_When_Within_Window_And_Below_Cap()
    {
        var resolved = Now.AddDays(-2);
        ReopenWindowMath.EvaluateEligibility(
                nowUtc: Now,
                resolvedAtUtc: resolved,
                reopenCount: 0,
                reopenWindowDays: 7,
                maxReopenCount: 3,
                reopenEnabled: true)
            .Should().Be(ReopenWindowMath.ReopenEligibility.Eligible);
    }

    [Fact]
    public void Eligible_Right_At_Window_Edge()
    {
        // resolved exactly window-days ago — boundary inclusive
        var resolved = Now.AddDays(-7);
        ReopenWindowMath.EvaluateEligibility(
                nowUtc: Now,
                resolvedAtUtc: resolved,
                reopenCount: 0,
                reopenWindowDays: 7,
                maxReopenCount: 3,
                reopenEnabled: true)
            .Should().Be(ReopenWindowMath.ReopenEligibility.Eligible);
    }

    [Fact]
    public void WindowClosed_When_Past_Window_End()
    {
        var resolved = Now.AddDays(-7).AddSeconds(-1);
        ReopenWindowMath.EvaluateEligibility(
                nowUtc: Now,
                resolvedAtUtc: resolved,
                reopenCount: 0,
                reopenWindowDays: 7,
                maxReopenCount: 3,
                reopenEnabled: true)
            .Should().Be(ReopenWindowMath.ReopenEligibility.WindowClosed);
    }

    [Fact]
    public void CountExceeded_When_Reopens_At_Cap()
    {
        var resolved = Now.AddDays(-1);
        ReopenWindowMath.EvaluateEligibility(
                nowUtc: Now,
                resolvedAtUtc: resolved,
                reopenCount: 3,
                reopenWindowDays: 7,
                maxReopenCount: 3,
                reopenEnabled: true)
            .Should().Be(ReopenWindowMath.ReopenEligibility.CountExceeded);
    }

    [Fact]
    public void CountExceeded_When_Reopens_Above_Cap()
    {
        ReopenWindowMath.EvaluateEligibility(
                nowUtc: Now,
                resolvedAtUtc: Now.AddDays(-1),
                reopenCount: 5,
                reopenWindowDays: 7,
                maxReopenCount: 3,
                reopenEnabled: true)
            .Should().Be(ReopenWindowMath.ReopenEligibility.CountExceeded);
    }

    [Fact]
    public void DisabledForMarket_When_Policy_Disabled()
    {
        ReopenWindowMath.EvaluateEligibility(
                nowUtc: Now,
                resolvedAtUtc: Now.AddDays(-1),
                reopenCount: 0,
                reopenWindowDays: 7,
                maxReopenCount: 3,
                reopenEnabled: false)
            .Should().Be(ReopenWindowMath.ReopenEligibility.DisabledForMarket);
    }

    [Fact]
    public void DisabledForMarket_When_Window_Days_Zero()
    {
        ReopenWindowMath.EvaluateEligibility(
                nowUtc: Now,
                resolvedAtUtc: Now.AddDays(-1),
                reopenCount: 0,
                reopenWindowDays: 0,
                maxReopenCount: 3,
                reopenEnabled: true)
            .Should().Be(ReopenWindowMath.ReopenEligibility.DisabledForMarket);
    }

    [Fact]
    public void DisabledForMarket_When_Cap_Zero()
    {
        ReopenWindowMath.EvaluateEligibility(
                nowUtc: Now,
                resolvedAtUtc: Now.AddDays(-1),
                reopenCount: 0,
                reopenWindowDays: 7,
                maxReopenCount: 0,
                reopenEnabled: true)
            .Should().Be(ReopenWindowMath.ReopenEligibility.DisabledForMarket);
    }

    [Fact]
    public void ResolvedAtMissing_Surfaces_Distinct_Verdict()
    {
        ReopenWindowMath.EvaluateEligibility(
                nowUtc: Now,
                resolvedAtUtc: null,
                reopenCount: 0,
                reopenWindowDays: 7,
                maxReopenCount: 3,
                reopenEnabled: true)
            .Should().Be(ReopenWindowMath.ReopenEligibility.ResolvedAtMissing);
    }

    [Theory]
    [InlineData(ReopenWindowMath.ReopenEligibility.Eligible, null)]
    [InlineData(ReopenWindowMath.ReopenEligibility.DisabledForMarket,
        TicketReasonCode.ReopenDisabledForMarket)]
    [InlineData(ReopenWindowMath.ReopenEligibility.WindowClosed,
        TicketReasonCode.ReopenWindowClosed)]
    [InlineData(ReopenWindowMath.ReopenEligibility.ResolvedAtMissing,
        TicketReasonCode.ReopenWindowClosed)]
    [InlineData(ReopenWindowMath.ReopenEligibility.CountExceeded,
        TicketReasonCode.ReopenCountExceeded)]
    public void ToReasonCode_Maps_Verdicts(
        ReopenWindowMath.ReopenEligibility verdict, string? expected)
    {
        ReopenWindowMath.ToReasonCode(verdict).Should().Be(expected);
    }

    [Fact]
    public void ComputeWindowEndUtc_Adds_Days()
    {
        var resolved = DateTimeOffset.Parse("2026-04-30T08:00:00+00:00");
        ReopenWindowMath.ComputeWindowEndUtc(resolved, 7)
            .Should().Be(DateTimeOffset.Parse("2026-05-07T08:00:00+00:00"));
    }

    [Fact]
    public void ComputeWindowEndUtc_Rejects_Negative_Days()
    {
        var resolved = DateTimeOffset.UtcNow;
        var act = () => ReopenWindowMath.ComputeWindowEndUtc(resolved, -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
