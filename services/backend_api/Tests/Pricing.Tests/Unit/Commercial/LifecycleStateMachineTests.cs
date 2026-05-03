using BackendApi.Modules.Pricing.Primitives.Commercial;
using FluentAssertions;

namespace Pricing.Tests.Unit.Commercial;

public sealed class LifecycleStateMachineTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Future = Now.AddDays(7);
    private static readonly DateTimeOffset Past = Now.AddDays(-1);

    [Fact]
    public void Schedule_FutureValidFrom_GoesToScheduled()
    {
        var ok = LifecycleStateMachine.TryTransition(
            LifecycleState.Draft, LifecycleTrigger.Schedule, Now, Future, Future.AddDays(7),
            out var to, out var reason);

        ok.Should().BeTrue();
        to.Should().Be(LifecycleState.Scheduled);
        reason.Should().BeNull();
    }

    [Fact]
    public void Schedule_PastValidFrom_GoesToActive()
    {
        var ok = LifecycleStateMachine.TryTransition(
            LifecycleState.Draft, LifecycleTrigger.Schedule, Now, Past, Future,
            out var to, out _);

        ok.Should().BeTrue();
        to.Should().Be(LifecycleState.Active);
    }

    [Fact]
    public void Schedule_MissingValidFrom_Rejects()
    {
        var ok = LifecycleStateMachine.TryTransition(
            LifecycleState.Draft, LifecycleTrigger.Schedule, Now, null, Future,
            out _, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("commercial.schedule.invalid_window");
    }

    [Fact]
    public void TimerActivate_FromScheduled_AfterValidFrom_GoesToActive()
    {
        var ok = LifecycleStateMachine.TryTransition(
            LifecycleState.Scheduled, LifecycleTrigger.TimerActivate, Now, Past, Future,
            out var to, out _);

        ok.Should().BeTrue();
        to.Should().Be(LifecycleState.Active);
    }

    [Fact]
    public void TimerActivate_BeforeValidFrom_Rejects()
    {
        var ok = LifecycleStateMachine.TryTransition(
            LifecycleState.Scheduled, LifecycleTrigger.TimerActivate, Now, Future, Future.AddDays(7),
            out _, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("commercial.timer.not_yet_due");
    }

    [Theory]
    [InlineData(LifecycleState.Scheduled)]
    [InlineData(LifecycleState.Active)]
    [InlineData(LifecycleState.Deactivated)]
    public void TimerExpire_AfterValidTo_GoesToExpired(LifecycleState from)
    {
        var ok = LifecycleStateMachine.TryTransition(
            from, LifecycleTrigger.TimerExpire, Now, Past, Past.AddHours(1),
            out var to, out _);

        ok.Should().BeTrue();
        to.Should().Be(LifecycleState.Expired);
    }

    [Theory]
    [InlineData(LifecycleState.Scheduled)]
    [InlineData(LifecycleState.Active)]
    public void Deactivate_FromLiveStates_GoesToDeactivated(LifecycleState from)
    {
        var ok = LifecycleStateMachine.TryTransition(
            from, LifecycleTrigger.Deactivate, Now, Past, Future,
            out var to, out _);

        ok.Should().BeTrue();
        to.Should().Be(LifecycleState.Deactivated);
    }

    [Fact]
    public void Reactivate_BeforeValidTo_GoesToActive()
    {
        var ok = LifecycleStateMachine.TryTransition(
            LifecycleState.Deactivated, LifecycleTrigger.Reactivate, Now, Past, Future,
            out var to, out _);

        ok.Should().BeTrue();
        to.Should().Be(LifecycleState.Active);
    }

    [Fact]
    public void Reactivate_AfterValidTo_RejectsExpiredTerminal()
    {
        var ok = LifecycleStateMachine.TryTransition(
            LifecycleState.Deactivated, LifecycleTrigger.Reactivate, Now, Past.AddDays(-30), Past.AddDays(-1),
            out _, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("commercial.reactivation.expired_terminal");
    }

    [Theory]
    [InlineData(LifecycleTrigger.Schedule)]
    [InlineData(LifecycleTrigger.TimerActivate)]
    [InlineData(LifecycleTrigger.Deactivate)]
    [InlineData(LifecycleTrigger.Reactivate)]
    [InlineData(LifecycleTrigger.AutoDeactivateBrokenReferences)]
    public void Expired_IsTerminal(LifecycleTrigger trigger)
    {
        var ok = LifecycleStateMachine.TryTransition(
            LifecycleState.Expired, trigger, Now, Past.AddDays(-30), Past.AddDays(-1),
            out _, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("commercial.row.expired_terminal");
    }

    [Fact]
    public void AutoDeactivateBrokenReferences_FromActive_GoesToDeactivated()
    {
        var ok = LifecycleStateMachine.TryTransition(
            LifecycleState.Active, LifecycleTrigger.AutoDeactivateBrokenReferences, Now, Past, Future,
            out var to, out _);

        ok.Should().BeTrue();
        to.Should().Be(LifecycleState.Deactivated);
    }
}
