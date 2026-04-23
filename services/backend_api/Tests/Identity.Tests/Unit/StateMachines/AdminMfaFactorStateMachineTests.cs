using BackendApi.Modules.Identity.Primitives.StateMachines;
using FluentAssertions;

namespace Identity.Tests.Unit.StateMachines;

public sealed class AdminMfaFactorStateMachineTests
{
    private readonly AdminMfaFactorStateMachine _sut = new();

    [Theory]
    [InlineData(AdminMfaFactorState.PendingConfirmation, AdminMfaFactorTrigger.Confirm, AdminMfaFactorState.Active)]
    [InlineData(AdminMfaFactorState.PendingConfirmation, AdminMfaFactorTrigger.Revoke, AdminMfaFactorState.Revoked)]
    [InlineData(AdminMfaFactorState.Active, AdminMfaFactorTrigger.StartRotation, AdminMfaFactorState.Rotating)]
    [InlineData(AdminMfaFactorState.Active, AdminMfaFactorTrigger.Revoke, AdminMfaFactorState.Revoked)]
    [InlineData(AdminMfaFactorState.Rotating, AdminMfaFactorTrigger.Confirm, AdminMfaFactorState.Active)]
    [InlineData(AdminMfaFactorState.Rotating, AdminMfaFactorTrigger.Revoke, AdminMfaFactorState.Revoked)]
    public void TryTransition_AllowsConfiguredTransitions(
        AdminMfaFactorState from,
        AdminMfaFactorTrigger trigger,
        AdminMfaFactorState expected)
    {
        var canTransition = _sut.TryTransition(from, trigger, out var next);

        canTransition.Should().BeTrue();
        next.Should().Be(expected);
    }

    [Fact]
    public void TryTransition_RejectsUnsupportedTransition()
    {
        var canTransition = _sut.TryTransition(AdminMfaFactorState.Revoked, AdminMfaFactorTrigger.Confirm, out var next);

        canTransition.Should().BeFalse();
        next.Should().Be(AdminMfaFactorState.Revoked);
    }
}
