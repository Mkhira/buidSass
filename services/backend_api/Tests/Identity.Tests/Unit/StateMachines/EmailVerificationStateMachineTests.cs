using BackendApi.Modules.Identity.Primitives.StateMachines;
using FluentAssertions;

namespace Identity.Tests.Unit.StateMachines;

public sealed class EmailVerificationStateMachineTests
{
    private readonly EmailVerificationStateMachine _sut = new();

    [Theory]
    [InlineData(EmailVerificationState.Pending, EmailVerificationTrigger.Confirm, EmailVerificationState.Completed)]
    [InlineData(EmailVerificationState.Pending, EmailVerificationTrigger.Expires, EmailVerificationState.Expired)]
    [InlineData(EmailVerificationState.Pending, EmailVerificationTrigger.Consume, EmailVerificationState.Consumed)]
    public void TryTransition_AllowsConfiguredTransitions(
        EmailVerificationState from,
        EmailVerificationTrigger trigger,
        EmailVerificationState expected)
    {
        var canTransition = _sut.TryTransition(from, trigger, out var next);

        canTransition.Should().BeTrue();
        next.Should().Be(expected);
    }

    [Fact]
    public void TryTransition_RejectsUnsupportedTransition()
    {
        var canTransition = _sut.TryTransition(EmailVerificationState.Completed, EmailVerificationTrigger.Confirm, out var next);

        canTransition.Should().BeFalse();
        next.Should().Be(EmailVerificationState.Completed);
    }
}
