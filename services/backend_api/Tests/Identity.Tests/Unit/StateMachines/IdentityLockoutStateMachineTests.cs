using BackendApi.Modules.Identity.Primitives.StateMachines;
using FluentAssertions;

namespace Identity.Tests.Unit.StateMachines;

public sealed class IdentityLockoutStateMachineTests
{
    private readonly IdentityLockoutStateMachine _sut = new();

    [Theory]
    [InlineData(IdentityLockoutState.Clear, IdentityLockoutTrigger.RegisterFailure, IdentityLockoutState.Tracking)]
    [InlineData(IdentityLockoutState.Tracking, IdentityLockoutTrigger.RegisterFailure, IdentityLockoutState.Tracking)]
    [InlineData(IdentityLockoutState.Tracking, IdentityLockoutTrigger.ThresholdReached, IdentityLockoutState.Locked)]
    [InlineData(IdentityLockoutState.Tracking, IdentityLockoutTrigger.Reset, IdentityLockoutState.Clear)]
    [InlineData(IdentityLockoutState.Locked, IdentityLockoutTrigger.WindowElapsed, IdentityLockoutState.Clear)]
    [InlineData(IdentityLockoutState.Locked, IdentityLockoutTrigger.Reset, IdentityLockoutState.Clear)]
    public void TryTransition_AllowsConfiguredTransitions(
        IdentityLockoutState from,
        IdentityLockoutTrigger trigger,
        IdentityLockoutState expected)
    {
        var canTransition = _sut.TryTransition(from, trigger, out var next);

        canTransition.Should().BeTrue();
        next.Should().Be(expected);
    }

    [Fact]
    public void TryTransition_RejectsUnsupportedTransition()
    {
        var canTransition = _sut.TryTransition(IdentityLockoutState.Clear, IdentityLockoutTrigger.WindowElapsed, out var next);

        canTransition.Should().BeFalse();
        next.Should().Be(IdentityLockoutState.Clear);
    }
}
