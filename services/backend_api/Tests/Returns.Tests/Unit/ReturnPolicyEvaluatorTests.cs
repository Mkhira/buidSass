using BackendApi.Modules.Returns.Primitives;
using FluentAssertions;

namespace Returns.Tests.Unit;

public class ReturnPolicyEvaluatorTests
{
    private readonly ReturnPolicyEvaluator _eval = new();
    private readonly DateTimeOffset _now = new(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Within_window_allowed()
    {
        var result = _eval.Evaluate(new PolicyEvaluationInput(
            DeliveredAt: _now.AddDays(-3),
            ProductZeroWindow: false,
            ReturnWindowDays: 14,
            NowUtc: _now));
        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void After_window_rejected()
    {
        var result = _eval.Evaluate(new PolicyEvaluationInput(
            DeliveredAt: _now.AddDays(-15),
            ProductZeroWindow: false,
            ReturnWindowDays: 14,
            NowUtc: _now));
        result.Allowed.Should().BeFalse();
        result.ReasonCode.Should().Be("return.window.expired");
    }

    [Fact]
    public void Restricted_zero_window_rejected_even_if_just_delivered()
    {
        var result = _eval.Evaluate(new PolicyEvaluationInput(
            DeliveredAt: _now.AddMinutes(-30),
            ProductZeroWindow: true,
            ReturnWindowDays: 14,
            NowUtc: _now));
        result.Allowed.Should().BeFalse();
        result.ReasonCode.Should().Be("return.line.restricted_zero_window");
    }

    [Fact]
    public void Not_delivered_yet_rejected()
    {
        var result = _eval.Evaluate(new PolicyEvaluationInput(
            DeliveredAt: null,
            ProductZeroWindow: false,
            ReturnWindowDays: 14,
            NowUtc: _now));
        result.Allowed.Should().BeFalse();
        result.ReasonCode.Should().Be("return.order.not_delivered");
    }

    [Fact]
    public void Zero_window_market_rejects()
    {
        var result = _eval.Evaluate(new PolicyEvaluationInput(
            DeliveredAt: _now.AddDays(-1),
            ProductZeroWindow: false,
            ReturnWindowDays: 0,
            NowUtc: _now));
        result.Allowed.Should().BeFalse();
        result.ReasonCode.Should().Be("return.window.expired");
    }
}
