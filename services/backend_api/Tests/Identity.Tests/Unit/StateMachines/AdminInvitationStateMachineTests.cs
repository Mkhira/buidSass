using BackendApi.Modules.Identity.Primitives.StateMachines;
using FluentAssertions;

namespace Identity.Tests.Unit.StateMachines;

public sealed class AdminInvitationStateMachineTests
{
    private readonly AdminInvitationStateMachine _sut = new();

    [Theory]
    [InlineData(AdminInvitationState.Pending, AdminInvitationTrigger.Accept, AdminInvitationState.Accepted)]
    [InlineData(AdminInvitationState.Pending, AdminInvitationTrigger.Revoke, AdminInvitationState.Revoked)]
    [InlineData(AdminInvitationState.Pending, AdminInvitationTrigger.Expires, AdminInvitationState.Expired)]
    public void TryTransition_AllowsConfiguredTransitions(
        AdminInvitationState from,
        AdminInvitationTrigger trigger,
        AdminInvitationState expected)
    {
        var canTransition = _sut.TryTransition(from, trigger, out var next);

        canTransition.Should().BeTrue();
        next.Should().Be(expected);
    }

    [Fact]
    public void TryTransition_RejectsUnsupportedTransition()
    {
        var canTransition = _sut.TryTransition(AdminInvitationState.Accepted, AdminInvitationTrigger.Accept, out var next);

        canTransition.Should().BeFalse();
        next.Should().Be(AdminInvitationState.Accepted);
    }
}
