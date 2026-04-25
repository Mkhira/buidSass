using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Primitives;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using FluentAssertions;

namespace Orders.Tests.Unit;

/// <summary>FR-009 / R11. Per-market return-eligibility window. Hardcoded launch defaults
/// (KSA=14d, EG=7d) until spec 013 ships and replaces the static table.</summary>
public sealed class ReturnEligibilityTests
{
    private static readonly ReturnEligibilityEvaluator Evaluator = new();

    [Fact]
    public void NotDelivered_ReturnsNotDeliveredCode()
    {
        var order = new Order
        {
            FulfillmentState = FulfillmentSm.HandedToCarrier,
            DeliveredAt = null,
            MarketCode = "KSA",
        };
        var result = Evaluator.Evaluate(order, DateTimeOffset.UtcNow);
        result.Eligible.Should().BeFalse();
        result.ReasonCode.Should().Be("order.return.not_delivered");
    }

    [Fact]
    public void DeliveredYesterday_KSA_Eligible_13DaysRemaining()
    {
        var deliveredAt = DateTimeOffset.UtcNow.AddDays(-1);
        var order = new Order
        {
            FulfillmentState = FulfillmentSm.Delivered,
            DeliveredAt = deliveredAt,
            MarketCode = "KSA",
        };
        var result = Evaluator.Evaluate(order, DateTimeOffset.UtcNow);
        result.Eligible.Should().BeTrue();
        result.DaysRemaining.Should().Be(13);
    }

    [Fact]
    public void Delivered10DaysAgo_EG_Expired()
    {
        var deliveredAt = DateTimeOffset.UtcNow.AddDays(-10);
        var order = new Order
        {
            FulfillmentState = FulfillmentSm.Delivered,
            DeliveredAt = deliveredAt,
            MarketCode = "EG",
        };
        var result = Evaluator.Evaluate(order, DateTimeOffset.UtcNow);
        result.Eligible.Should().BeFalse();
        result.ReasonCode.Should().Be("returnWindow.expired");
        result.DaysRemaining.Should().Be(0);
    }

    [Fact]
    public void UnknownMarket_FallsBackTo14Days()
    {
        var deliveredAt = DateTimeOffset.UtcNow.AddDays(-3);
        var order = new Order
        {
            FulfillmentState = FulfillmentSm.Delivered,
            DeliveredAt = deliveredAt,
            MarketCode = "UAE", // not in launch table
        };
        var result = Evaluator.Evaluate(order, DateTimeOffset.UtcNow);
        result.Eligible.Should().BeTrue();
        result.DaysRemaining.Should().Be(11);
    }
}
