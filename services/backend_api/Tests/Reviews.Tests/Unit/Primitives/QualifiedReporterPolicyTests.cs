using BackendApi.Modules.Reviews.Primitives;
using FluentAssertions;

namespace Reviews.Tests.Unit.Primitives;

/// <summary>Spec 022 T045 — every threshold combination of FR-023.</summary>
public sealed class QualifiedReporterPolicyTests
{
    [Theory]
    [InlineData(14, true, true)]   // exactly at age + has delivered → qualifies
    [InlineData(15, true, true)]   // above age + has delivered → qualifies
    [InlineData(13, true, false)]  // below age threshold → not qualified
    [InlineData(30, false, false)] // above age but not a verified buyer → not qualified
    [InlineData(0, true, false)]   // brand-new account → not qualified
    public void Default_policy_matches_table(int ageDays, bool hasDelivered, bool expected)
    {
        var policy = ReviewMarketPolicy.Default("SA");
        var facts = new QualifiedReporterPolicy.ReporterFacts(ageDays, hasDelivered);
        QualifiedReporterPolicy.Evaluate(facts, policy).Should().Be(expected);
    }

    [Fact]
    public void Verified_buyer_requirement_can_be_relaxed_per_market()
    {
        var policy = ReviewMarketPolicy.Default("SA") with { ReportQualifyingRequiresVerifiedBuyer = false };
        var facts = new QualifiedReporterPolicy.ReporterFacts(60, false);
        QualifiedReporterPolicy.Evaluate(facts, policy).Should().BeTrue();
    }

    [Fact]
    public void Account_age_threshold_is_market_tunable()
    {
        var policy = ReviewMarketPolicy.Default("EG") with { ReportQualifyingAccountAgeDays = 0 };
        var facts = new QualifiedReporterPolicy.ReporterFacts(0, true);
        QualifiedReporterPolicy.Evaluate(facts, policy).Should().BeTrue();
    }
}
