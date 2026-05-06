using BackendApi.Modules.Support.Primitives;
using FluentAssertions;

namespace Support.Tests.Unit;

/// <summary>
/// T048 — covers the legal-transition table for the 5-state ticket lifecycle
/// including the reopen edge <c>Resolved → InProgress</c>; rejects every
/// other transition with a stable reason code.
/// </summary>
public class TicketStateMachineTests
{
    [Fact]
    public void OpenToInProgress_ViaAgentClaim_IsAllowed()
    {
        var ok = TicketStateMachine.TryTransition(
            TicketState.Open, TicketState.InProgress,
            TicketTriggerKind.AgentClaim, TicketActorKind.Agent,
            out var reason);

        ok.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void InProgressToWaitingCustomer_ViaAgentReply_IsAllowed()
    {
        var ok = TicketStateMachine.TryTransition(
            TicketState.InProgress, TicketState.WaitingCustomer,
            TicketTriggerKind.AgentReply, TicketActorKind.Agent,
            out _);

        ok.Should().BeTrue();
    }

    [Fact]
    public void WaitingCustomerToInProgress_ViaCustomerReply_IsAllowed()
    {
        var ok = TicketStateMachine.TryTransition(
            TicketState.WaitingCustomer, TicketState.InProgress,
            TicketTriggerKind.CustomerReply, TicketActorKind.Customer,
            out _);

        ok.Should().BeTrue();
    }

    [Fact]
    public void InProgressToResolved_ViaAgentResolve_IsAllowed()
    {
        var ok = TicketStateMachine.TryTransition(
            TicketState.InProgress, TicketState.Resolved,
            TicketTriggerKind.AgentResolve, TicketActorKind.Agent,
            out _);

        ok.Should().BeTrue();
    }

    [Fact]
    public void ResolvedToInProgress_ViaCustomerReopen_IsAllowed()
    {
        var ok = TicketStateMachine.TryTransition(
            TicketState.Resolved, TicketState.InProgress,
            TicketTriggerKind.CustomerReopen, TicketActorKind.Customer,
            out _);

        ok.Should().BeTrue();
    }

    [Fact]
    public void ResolvedToClosed_ViaAutoClose_IsAllowed()
    {
        var ok = TicketStateMachine.TryTransition(
            TicketState.Resolved, TicketState.Closed,
            TicketTriggerKind.AutoCloseResolutionWindow, TicketActorKind.System,
            out _);

        ok.Should().BeTrue();
    }

    [Fact]
    public void ClosedIsTerminal_NoTransitionAllowed()
    {
        var ok = TicketStateMachine.TryTransition(
            TicketState.Closed, TicketState.InProgress,
            TicketTriggerKind.CustomerReopen, TicketActorKind.Customer,
            out var reason);

        ok.Should().BeFalse();
        reason.Should().Be(TicketReasonCode.ClosedTerminal);
    }

    [Fact]
    public void IdempotentTransition_IsTreatedAsLegalNoOp()
    {
        var ok = TicketStateMachine.TryTransition(
            TicketState.InProgress, TicketState.InProgress,
            TicketTriggerKind.AgentReply, TicketActorKind.Agent,
            out var reason);

        ok.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void OpenToResolved_DirectJump_IsRejected()
    {
        var ok = TicketStateMachine.TryTransition(
            TicketState.Open, TicketState.Resolved,
            TicketTriggerKind.AgentResolve, TicketActorKind.Agent,
            out var reason);

        ok.Should().BeFalse();
        reason.Should().Be(TicketReasonCode.InvalidTransition);
    }

    [Fact]
    public void CustomerCannotForceClose()
    {
        var ok = TicketStateMachine.TryTransition(
            TicketState.InProgress, TicketState.Closed,
            TicketTriggerKind.LeadForceClose, TicketActorKind.Customer,
            out var reason);

        ok.Should().BeFalse();
        reason.Should().Be(TicketReasonCode.InvalidTransition);
    }

    [Fact]
    public void IsTerminal_OnlyClosed()
    {
        TicketStateMachine.IsTerminal(TicketState.Closed).Should().BeTrue();
        TicketStateMachine.IsTerminal(TicketState.Resolved).Should().BeFalse();
        TicketStateMachine.IsTerminal(TicketState.InProgress).Should().BeFalse();
    }

    [Fact]
    public void AcceptsCustomerReply_RejectsResolvedAndClosed()
    {
        TicketStateMachine.AcceptsCustomerReply(TicketState.Open).Should().BeTrue();
        TicketStateMachine.AcceptsCustomerReply(TicketState.InProgress).Should().BeTrue();
        TicketStateMachine.AcceptsCustomerReply(TicketState.WaitingCustomer).Should().BeTrue();
        TicketStateMachine.AcceptsCustomerReply(TicketState.Resolved).Should().BeFalse();
        TicketStateMachine.AcceptsCustomerReply(TicketState.Closed).Should().BeFalse();
    }
}
