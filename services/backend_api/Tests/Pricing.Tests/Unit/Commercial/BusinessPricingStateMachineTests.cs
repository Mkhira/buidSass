using BackendApi.Modules.Pricing.Primitives.Commercial;
using FluentAssertions;

namespace Pricing.Tests.Unit.Commercial;

public sealed class BusinessPricingStateMachineTests
{
    [Theory]
    [InlineData(BusinessPricingTrigger.Deactivate)]
    [InlineData(BusinessPricingTrigger.AutoDeactivateBrokenCompany)]
    public void Active_To_Deactivated(BusinessPricingTrigger trigger)
    {
        var ok = BusinessPricingStateMachine.TryTransition(
            BusinessPricingState.Active, trigger, out var to, out _);

        ok.Should().BeTrue();
        to.Should().Be(BusinessPricingState.Deactivated);
    }

    [Fact]
    public void Deactivated_To_Active()
    {
        var ok = BusinessPricingStateMachine.TryTransition(
            BusinessPricingState.Deactivated, BusinessPricingTrigger.Reactivate, out var to, out _);

        ok.Should().BeTrue();
        to.Should().Be(BusinessPricingState.Active);
    }

    [Fact]
    public void Active_Reactivate_Rejected()
    {
        var ok = BusinessPricingStateMachine.TryTransition(
            BusinessPricingState.Active, BusinessPricingTrigger.Reactivate, out _, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("business_pricing.row.invalid_transition");
    }

    [Fact]
    public void Deactivated_Deactivate_Rejected()
    {
        var ok = BusinessPricingStateMachine.TryTransition(
            BusinessPricingState.Deactivated, BusinessPricingTrigger.Deactivate, out _, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("business_pricing.row.invalid_transition");
    }
}
