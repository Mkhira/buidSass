using BackendApi.Modules.Shipping.Domain.StateMachines;
using BackendApi.Modules.Shipping.Primitives;
using FluentAssertions;

namespace Shipping.Tests.Unit;

public sealed class ShipmentStateMachineTests
{
    // AC-12 — out-of-order webhook precedence (BR-12). Higher precedence wins,
    // lower precedence events do not regress state.
    [Theory]
    [InlineData(ShipmentStates.InTransit, ShipmentStates.Delivered, true)]
    [InlineData(ShipmentStates.OutForDelivery, ShipmentStates.Delivered, true)]
    [InlineData(ShipmentStates.Delivered, ShipmentStates.InTransit, false)]
    [InlineData(ShipmentStates.Delivered, ShipmentStates.OutForDelivery, false)]
    [InlineData(ShipmentStates.HandedToCarrier, ShipmentStates.InTransit, true)]
    [InlineData(ShipmentStates.InTransit, ShipmentStates.HandedToCarrier, false)]
    public void ShouldApply_respects_precedence(string current, string proposed, bool expected)
    {
        ShipmentStateMachine.ShouldApply(current, proposed).Should().Be(expected);
    }

    [Fact]
    public void Transitions_pending_to_label_purchased_allowed()
    {
        ShipmentStateMachine.CanTransition(ShipmentStates.Pending, ShipmentStates.LabelPurchased)
            .Should().BeTrue();
    }

    [Fact]
    public void Transitions_delivered_to_disputed_allowed()
    {
        ShipmentStateMachine.CanTransition(ShipmentStates.Delivered, ShipmentStates.DeliveryDisputed)
            .Should().BeTrue();
    }

    [Fact]
    public void Terminal_states_have_no_outgoing_transitions()
    {
        ShipmentStateMachine.AllowedTo(ShipmentStates.ReturnedToSender).Should().BeEmpty();
        ShipmentStateMachine.AllowedTo(ShipmentStates.ClosedWithRefund).Should().BeEmpty();
        ShipmentStateMachine.AllowedTo(ShipmentStates.LabelVoided).Should().BeEmpty();
    }

    [Fact]
    public void EnsureTransition_throws_on_invalid()
    {
        var act = () => ShipmentStateMachine.EnsureTransition(ShipmentStates.Pending, ShipmentStates.Delivered);
        act.Should().Throw<InvalidOperationException>();
    }
}
