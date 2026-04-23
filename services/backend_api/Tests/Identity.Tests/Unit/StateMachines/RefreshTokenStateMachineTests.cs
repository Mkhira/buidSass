using BackendApi.Modules.Identity.Primitives.StateMachines;
using FluentAssertions;

namespace Identity.Tests.Unit.StateMachines;

public sealed class RefreshTokenStateMachineTests
{
    private readonly RefreshTokenStateMachine _sut = new();

    [Theory]
    [InlineData(RefreshTokenState.Active, RefreshTokenTrigger.Consume, RefreshTokenState.Consumed)]
    [InlineData(RefreshTokenState.Active, RefreshTokenTrigger.Revoke, RefreshTokenState.Revoked)]
    [InlineData(RefreshTokenState.Active, RefreshTokenTrigger.Expire, RefreshTokenState.Expired)]
    [InlineData(RefreshTokenState.Consumed, RefreshTokenTrigger.Revoke, RefreshTokenState.Revoked)]
    public void TryTransition_AllowsConfiguredTransitions(
        RefreshTokenState from,
        RefreshTokenTrigger trigger,
        RefreshTokenState expected)
    {
        var canTransition = _sut.TryTransition(from, trigger, out var next);

        canTransition.Should().BeTrue();
        next.Should().Be(expected);
    }

    [Fact]
    public void TryTransition_RejectsUnsupportedTransition()
    {
        var canTransition = _sut.TryTransition(RefreshTokenState.Revoked, RefreshTokenTrigger.Consume, out var next);

        canTransition.Should().BeFalse();
        next.Should().Be(RefreshTokenState.Revoked);
    }
}
