using BackendApi.Modules.Reviews.Primitives;
using FluentAssertions;

namespace Reviews.Tests.Unit.Primitives;

/// <summary>
/// Spec 022 T044 — every legal transition in data-model §3 returns true,
/// every illegal transition returns false with a stable reason code.
/// </summary>
public sealed class ReviewStateMachineTests
{
    [Theory]
    [InlineData(ReviewState.PendingModeration, ReviewState.Visible, ReviewTriggerKind.ModeratorAction, ReviewActorKind.Moderator)]
    [InlineData(ReviewState.PendingModeration, ReviewState.Hidden, ReviewTriggerKind.ModeratorAction, ReviewActorKind.Moderator)]
    [InlineData(ReviewState.Visible, ReviewState.PendingModeration, ReviewTriggerKind.CustomerEdit, ReviewActorKind.Customer)]
    [InlineData(ReviewState.Visible, ReviewState.Flagged, ReviewTriggerKind.CommunityReportThreshold, ReviewActorKind.System)]
    [InlineData(ReviewState.Visible, ReviewState.Hidden, ReviewTriggerKind.ModeratorAction, ReviewActorKind.Moderator)]
    [InlineData(ReviewState.Visible, ReviewState.Hidden, ReviewTriggerKind.RefundEvent, ReviewActorKind.System)]
    [InlineData(ReviewState.Visible, ReviewState.Hidden, ReviewTriggerKind.AccountLocked, ReviewActorKind.System)]
    [InlineData(ReviewState.Flagged, ReviewState.Visible, ReviewTriggerKind.ModeratorAction, ReviewActorKind.Moderator)]
    [InlineData(ReviewState.Flagged, ReviewState.Hidden, ReviewTriggerKind.ModeratorAction, ReviewActorKind.Moderator)]
    [InlineData(ReviewState.Flagged, ReviewState.Hidden, ReviewTriggerKind.RefundEvent, ReviewActorKind.System)]
    [InlineData(ReviewState.Hidden, ReviewState.Visible, ReviewTriggerKind.ModeratorAction, ReviewActorKind.Moderator)]
    [InlineData(ReviewState.Hidden, ReviewState.Deleted, ReviewTriggerKind.ManualSuperAdmin, ReviewActorKind.SuperAdmin)]
    [InlineData(ReviewState.Visible, ReviewState.Deleted, ReviewTriggerKind.ManualSuperAdmin, ReviewActorKind.SuperAdmin)]
    [InlineData(ReviewState.Flagged, ReviewState.Deleted, ReviewTriggerKind.ManualSuperAdmin, ReviewActorKind.SuperAdmin)]
    [InlineData(ReviewState.PendingModeration, ReviewState.Deleted, ReviewTriggerKind.ManualSuperAdmin, ReviewActorKind.SuperAdmin)]
    public void Legal_transitions_return_true(ReviewState from, ReviewState to, string trigger, ReviewActorKind actor)
    {
        var ok = ReviewStateMachine.TryTransition(from, to, trigger, actor, out var reason);
        ok.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Theory]
    [InlineData(ReviewState.Deleted, ReviewState.Visible, ReviewTriggerKind.ManualSuperAdmin, ReviewActorKind.SuperAdmin, "reviews.moderation.delete_terminal")]
    [InlineData(ReviewState.Deleted, ReviewState.Hidden, ReviewTriggerKind.ModeratorAction, ReviewActorKind.Moderator, "reviews.moderation.delete_terminal")]
    [InlineData(ReviewState.Visible, ReviewState.Deleted, ReviewTriggerKind.ModeratorAction, ReviewActorKind.Moderator, "reviews.moderation.invalid_state")]
    [InlineData(ReviewState.Visible, ReviewState.Hidden, ReviewTriggerKind.ModeratorAction, ReviewActorKind.Customer, "reviews.moderation.invalid_state")]
    [InlineData(ReviewState.Hidden, ReviewState.Flagged, ReviewTriggerKind.ModeratorAction, ReviewActorKind.Moderator, "reviews.moderation.invalid_state")]
    public void Illegal_transitions_return_false_with_reason(
        ReviewState from, ReviewState to, string trigger, ReviewActorKind actor, string expected)
    {
        var ok = ReviewStateMachine.TryTransition(from, to, trigger, actor, out var reason);
        ok.Should().BeFalse();
        reason.Should().Be(expected);
    }

    [Theory]
    [InlineData(ReviewState.Visible, ReviewState.Visible)]
    [InlineData(ReviewState.PendingModeration, ReviewState.PendingModeration)]
    [InlineData(ReviewState.Hidden, ReviewState.Hidden)]
    public void Idempotent_transitions_are_treated_as_legal(ReviewState s, ReviewState t)
    {
        var ok = ReviewStateMachine.TryTransition(s, t, ReviewTriggerKind.ModeratorAction, ReviewActorKind.Moderator, out _);
        ok.Should().BeTrue();
    }

    [Theory]
    [InlineData(ReviewState.Visible, ReviewState.Hidden, true)]               // counted → not counted
    [InlineData(ReviewState.Flagged, ReviewState.Hidden, true)]               // counted → not counted
    [InlineData(ReviewState.PendingModeration, ReviewState.Visible, true)]    // not counted → counted
    [InlineData(ReviewState.Hidden, ReviewState.Visible, true)]               // not counted → counted
    [InlineData(ReviewState.Visible, ReviewState.Flagged, false)]             // both counted: aggregate row count unchanged
    public void Aggregate_affecting_transitions_flag_correctly(ReviewState from, ReviewState to, bool affects)
    {
        // Visible↔Flagged both count; transition does not affect aggregate values.
        // Flagged↔Visible returns false (both in counted set); the others flip
        // membership and return true.
        ReviewStateMachine.TransitionAffectsAggregate(from, to).Should().Be(affects);
    }

    [Fact]
    public void Deleted_is_terminal()
    {
        ReviewStateMachine.IsTerminal(ReviewState.Deleted).Should().BeTrue();
        ReviewStateMachine.IsTerminal(ReviewState.Visible).Should().BeFalse();
        ReviewStateMachine.IsTerminal(ReviewState.Hidden).Should().BeFalse();
    }
}
