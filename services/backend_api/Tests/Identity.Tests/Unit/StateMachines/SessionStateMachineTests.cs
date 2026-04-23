using BackendApi.Modules.Identity.Primitives.StateMachines;
using FluentAssertions;

namespace Identity.Tests.Unit.StateMachines;

public sealed class SessionStateMachineTests
{
    private readonly SessionStateMachine _sut = new();

    [Theory]
    [InlineData(SessionState.Active, SessionTrigger.Revoke, SessionState.Revoked)]
    [InlineData(SessionState.Active, SessionTrigger.Expire, SessionState.Expired)]
    public void TryTransition_AllowsConfiguredTransitions(
        SessionState from,
        SessionTrigger trigger,
        SessionState expected)
    {
        var canTransition = _sut.TryTransition(from, trigger, out var next);

        canTransition.Should().BeTrue();
        next.Should().Be(expected);
    }

    [Fact]
    public void TryTransition_RejectsUnsupportedTransition()
    {
        var canTransition = _sut.TryTransition(SessionState.Revoked, SessionTrigger.Revoke, out var next);

        canTransition.Should().BeFalse();
        next.Should().Be(SessionState.Revoked);
    }
}
