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
        // Per spec 007-b T010 / contract §11 the owned-code surface is 49 codes.
        CommercialReasonCode.AllCodes.Count.Should().Be(49);
    }

    [Fact]
    public void AllCodes_FollowDottedNamespace()
    {
        CommercialReasonCode.AllCodes.Should().AllSatisfy(code =>
            code.Should().Contain(".",
                "every reason code uses dotted namespacing for ICU-key resolution"));
    }
}
