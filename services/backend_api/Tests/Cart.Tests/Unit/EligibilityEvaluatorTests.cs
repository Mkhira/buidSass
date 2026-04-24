using BackendApi.Modules.Cart.Primitives;
using FluentAssertions;

namespace Cart.Tests.Unit;

public sealed class EligibilityEvaluatorTests
{
    private static readonly EligibilityEvaluator Evaluator = new();

    private static EligibilityEvaluator.Input Input(
        bool hasRestricted = false,
        string? restrictedReason = null,
        bool verified = false,
        bool hasUnavailable = false,
        bool hasShortfall = false,
        bool hasB2BOnly = false,
        bool isB2B = false,
        int lineCount = 1)
        => new(hasRestricted, restrictedReason, verified, hasUnavailable, hasShortfall, hasB2BOnly, isB2B, lineCount);

    [Fact]
    public void EmptyCart_Disallowed_WithCartEmpty()
    {
        var r = Evaluator.Evaluate(Input(lineCount: 0));
        r.Allowed.Should().BeFalse();
        r.ReasonCode.Should().Be("cart.empty");
    }

    [Fact]
    public void UnavailableLine_BeatsInventoryShortfall()
    {
        var r = Evaluator.Evaluate(Input(hasUnavailable: true, hasShortfall: true));
        r.Allowed.Should().BeFalse();
        r.ReasonCode.Should().Be("cart.line_unavailable");
    }

    [Fact]
    public void InventoryShortfall_BeatsRestriction()
    {
        var r = Evaluator.Evaluate(Input(
            hasRestricted: true,
            restrictedReason: "catalog.restricted.verification_required",
            hasShortfall: true));
        r.Allowed.Should().BeFalse();
        r.ReasonCode.Should().Be("cart.inventory_insufficient");
    }

    [Fact]
    public void RestrictedAndVerified_Allows()
    {
        var r = Evaluator.Evaluate(Input(
            hasRestricted: true,
            restrictedReason: "catalog.restricted.verification_required",
            verified: true));
        r.Allowed.Should().BeTrue();
        r.ReasonCode.Should().BeNull();
    }

    [Fact]
    public void RestrictedAndNotVerified_ReturnsProvidedReasonCode()
    {
        var r = Evaluator.Evaluate(Input(
            hasRestricted: true,
            restrictedReason: "catalog.restricted.professional_only"));
        r.Allowed.Should().BeFalse();
        r.ReasonCode.Should().Be("catalog.restricted.professional_only");
    }

    [Fact]
    public void RestrictedMissingReasonCode_FallsBackToDefault()
    {
        var r = Evaluator.Evaluate(Input(hasRestricted: true));
        r.Allowed.Should().BeFalse();
        r.ReasonCode.Should().Be("catalog.restricted.verification_required");
    }

    [Fact]
    public void CleanCart_Allowed()
    {
        var r = Evaluator.Evaluate(Input(lineCount: 3));
        r.Allowed.Should().BeTrue();
        r.ReasonCode.Should().BeNull();
    }

    [Fact]
    public void B2BOnlyLine_NonB2BCustomer_Blocked()
    {
        var r = Evaluator.Evaluate(Input(hasB2BOnly: true, isB2B: false));
        r.Allowed.Should().BeFalse();
        r.ReasonCode.Should().Be("cart.b2b_required");
    }

    [Fact]
    public void B2BOnlyLine_B2BCustomer_Allowed()
    {
        var r = Evaluator.Evaluate(Input(hasB2BOnly: true, isB2B: true));
        r.Allowed.Should().BeTrue();
    }
}
