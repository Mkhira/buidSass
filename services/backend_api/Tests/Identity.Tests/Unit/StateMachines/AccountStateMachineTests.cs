using BackendApi.Modules.Identity.Primitives.StateMachines;
using FluentAssertions;

namespace Identity.Tests.Unit.StateMachines;

public sealed class AccountStateMachineTests
{
    private readonly AccountStateMachine _sut = new();

    [Theory]
    [InlineData(AccountState.PendingEmailVerification, AccountTrigger.ConfirmEmail, AccountState.Active)]
    [InlineData(AccountState.PendingEmailVerification, AccountTrigger.Delete, AccountState.Deleted)]
    [InlineData(AccountState.PendingPasswordRotation, AccountTrigger.CompletePasswordRotation, AccountState.Active)]
    [InlineData(AccountState.PendingPasswordRotation, AccountTrigger.Disable, AccountState.Disabled)]
    [InlineData(AccountState.PendingPasswordRotation, AccountTrigger.Delete, AccountState.Deleted)]
    [InlineData(AccountState.Active, AccountTrigger.Lock, AccountState.Locked)]
    [InlineData(AccountState.Active, AccountTrigger.Disable, AccountState.Disabled)]
    [InlineData(AccountState.Active, AccountTrigger.RequirePasswordRotation, AccountState.PendingPasswordRotation)]
    [InlineData(AccountState.Active, AccountTrigger.Delete, AccountState.Deleted)]
    [InlineData(AccountState.Locked, AccountTrigger.Unlock, AccountState.Active)]
    [InlineData(AccountState.Locked, AccountTrigger.Disable, AccountState.Disabled)]
    [InlineData(AccountState.Locked, AccountTrigger.Delete, AccountState.Deleted)]
    [InlineData(AccountState.Disabled, AccountTrigger.Enable, AccountState.Active)]
    [InlineData(AccountState.Disabled, AccountTrigger.Delete, AccountState.Deleted)]
    public void TryTransition_AllowsConfiguredTransitions(
        AccountState from,
        AccountTrigger trigger,
        AccountState expected)
    {
        var canTransition = _sut.TryTransition(from, trigger, out var next);

        canTransition.Should().BeTrue();
        next.Should().Be(expected);
    }

    [Fact]
    public void TryTransition_RejectsUnsupportedTransition()
    {
        var canTransition = _sut.TryTransition(AccountState.Deleted, AccountTrigger.Enable, out var next);

        canTransition.Should().BeFalse();
        next.Should().Be(AccountState.Deleted);
    }
}
