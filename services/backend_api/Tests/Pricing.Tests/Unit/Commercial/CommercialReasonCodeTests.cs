using BackendApi.Modules.Pricing.Primitives.Commercial;
using FluentAssertions;

namespace Pricing.Tests.Unit.Commercial;

public sealed class CommercialReasonCodeTests
{
    [Fact]
    public void AllCodes_AreDistinct()
    {
        var distinct = CommercialReasonCode.AllCodes.Distinct().ToList();
        distinct.Count.Should().Be(CommercialReasonCode.AllCodes.Count,
            "every owned code must be unique (T010 / contract §11)");
    }

    [Fact]
    public void AllCodes_AreNonEmpty()
    {
        CommercialReasonCode.AllCodes.Should().AllSatisfy(code =>
            code.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void AllCodes_ContainsExpectedCount()
    {
        // Per spec 007-b T010 / contract §11 the owned-code surface is 49
        // codes at launch. PR #80 round 1 added `BusinessPricingValidationError`
        // (50 total) to disambiguate validation failures from row-conflict
        // (duplicate) errors per CodeRabbit feedback.
        CommercialReasonCode.AllCodes.Count.Should().Be(50);
    }

    [Fact]
    public void AllCodes_FollowDottedNamespace()
    {
        CommercialReasonCode.AllCodes.Should().AllSatisfy(code =>
            code.Should().Contain(".",
                "every reason code uses dotted namespacing for ICU-key resolution"));
    }
}
