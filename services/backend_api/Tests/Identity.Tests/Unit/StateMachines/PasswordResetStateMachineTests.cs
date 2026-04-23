using BackendApi.Modules.Identity.Primitives.StateMachines;
using FluentAssertions;

namespace Identity.Tests.Unit.StateMachines;

public sealed class PasswordResetStateMachineTests
{
    private readonly PasswordResetStateMachine _sut = new();

    [Theory]
    [InlineData(PasswordResetState.Pending, PasswordResetTrigger.Consume, PasswordResetState.Consumed)]
    [InlineData(PasswordResetState.Pending, PasswordResetTrigger.Expires, PasswordResetState.Expired)]
    [InlineData(PasswordResetState.Pending, PasswordResetTrigger.Revoke, PasswordResetState.Revoked)]
    public void TryTransition_AllowsConfiguredTransitions(
        PasswordResetState from,
        PasswordResetTrigger trigger,
        PasswordResetState expected)
    {
        var canTransition = _sut.TryTransition(from, trigger, out var next);

        canTransition.Should().BeTrue();
        next.Should().Be(expected);
    }

    [Fact]
    public void TryTransition_RejectsUnsupportedTransition()
    {
        var canTransition = _sut.TryTransition(PasswordResetState.Consumed, PasswordResetTrigger.Consume, out var next);

        canTransition.Should().BeFalse();
        next.Should().Be(PasswordResetState.Consumed);
    }
}
