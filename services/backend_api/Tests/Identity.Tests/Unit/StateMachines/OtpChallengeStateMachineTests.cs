using BackendApi.Modules.Identity.Primitives.StateMachines;
using FluentAssertions;

namespace Identity.Tests.Unit.StateMachines;

public sealed class OtpChallengeStateMachineTests
{
    private readonly OtpChallengeStateMachine _sut = new();

    [Theory]
    [InlineData(OtpChallengeState.Pending, OtpChallengeTrigger.VerifySuccess, OtpChallengeState.Completed)]
    [InlineData(OtpChallengeState.Pending, OtpChallengeTrigger.Expires, OtpChallengeState.Expired)]
    [InlineData(OtpChallengeState.Pending, OtpChallengeTrigger.MaxAttemptsReached, OtpChallengeState.Exhausted)]
    [InlineData(OtpChallengeState.Pending, OtpChallengeTrigger.Revoke, OtpChallengeState.Revoked)]
    public void TryTransition_AllowsConfiguredTransitions(
        OtpChallengeState from,
        OtpChallengeTrigger trigger,
        OtpChallengeState expected)
    {
        var canTransition = _sut.TryTransition(from, trigger, out var next);

        canTransition.Should().BeTrue();
        next.Should().Be(expected);
    }

    [Fact]
    public void TryTransition_RejectsUnsupportedTransition()
    {
        var canTransition = _sut.TryTransition(OtpChallengeState.Completed, OtpChallengeTrigger.Revoke, out var next);

        canTransition.Should().BeFalse();
        next.Should().Be(OtpChallengeState.Completed);
    }
}
