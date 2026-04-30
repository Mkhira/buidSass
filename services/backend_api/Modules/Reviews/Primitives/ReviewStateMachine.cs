namespace BackendApi.Modules.Reviews.Primitives;

/// <summary>
/// Pure-function state-machine guard. Encodes the transition table from
/// data-model §3. Returns <see langword="false"/> with a stable reason code
/// for every illegal transition; the caller turns the reason code into a 400/403.
/// </summary>
public static class ReviewStateMachine
{
    /// <summary>
    /// Validates a proposed transition. Idempotent (from == to) is treated as legal
    /// no-op; the caller is responsible for short-circuiting before writing audit.
    /// </summary>
    /// <returns><see langword="true"/> when allowed; <see langword="false"/> + reason code when not.</returns>
    public static bool TryTransition(
        ReviewState from,
        ReviewState to,
        string trigger,
        ReviewActorKind actor,
        out string? reasonCode)
    {
        reasonCode = null;

        if (from == ReviewState.Deleted)
        {
            reasonCode = "reviews.moderation.delete_terminal";
            return false;
        }

        if (from == to)
        {
            return true;
        }

        if (!ReviewTriggerKind.All.Contains(trigger))
        {
            reasonCode = "reviews.moderation.invalid_state";
            return false;
        }

        switch ((from, to))
        {
            case (ReviewState.PendingModeration, ReviewState.Visible)
                when trigger == ReviewTriggerKind.ModeratorAction && actor == ReviewActorKind.Moderator:
            case (ReviewState.PendingModeration, ReviewState.Hidden)
                when trigger == ReviewTriggerKind.ModeratorAction && actor == ReviewActorKind.Moderator:
            case (ReviewState.Visible, ReviewState.PendingModeration)
                when trigger == ReviewTriggerKind.CustomerEdit && actor == ReviewActorKind.Customer:
            case (ReviewState.Visible, ReviewState.Flagged)
                when trigger == ReviewTriggerKind.CommunityReportThreshold && actor == ReviewActorKind.System:
            case (ReviewState.Visible, ReviewState.Hidden)
                when (trigger == ReviewTriggerKind.ModeratorAction && actor == ReviewActorKind.Moderator)
                  || (trigger == ReviewTriggerKind.RefundEvent && actor == ReviewActorKind.System)
                  || (trigger == ReviewTriggerKind.AccountLocked && actor == ReviewActorKind.System):
            case (ReviewState.Flagged, ReviewState.Visible)
                when trigger == ReviewTriggerKind.ModeratorAction && actor == ReviewActorKind.Moderator:
            case (ReviewState.Flagged, ReviewState.Hidden)
                when (trigger == ReviewTriggerKind.ModeratorAction && actor == ReviewActorKind.Moderator)
                  || (trigger == ReviewTriggerKind.RefundEvent && actor == ReviewActorKind.System)
                  || (trigger == ReviewTriggerKind.AccountLocked && actor == ReviewActorKind.System):
            case (ReviewState.Hidden, ReviewState.Visible)
                when trigger == ReviewTriggerKind.ModeratorAction && actor == ReviewActorKind.Moderator:
                return true;

            case (_, ReviewState.Deleted)
                when trigger == ReviewTriggerKind.ManualSuperAdmin && actor == ReviewActorKind.SuperAdmin:
                return true;

            default:
                reasonCode = "reviews.moderation.invalid_state";
                return false;
        }
    }

    /// <summary>
    /// True for states whose rows feed the <c>product_rating_aggregates</c> denormalized read-side.
    /// Per data-model §3 only <see cref="ReviewState.Visible"/> + <see cref="ReviewState.Flagged"/>
    /// count toward avg / distribution.
    /// </summary>
    public static bool CountsInAggregate(ReviewState state) =>
        state == ReviewState.Visible || state == ReviewState.Flagged;

    /// <summary>
    /// True when the rating aggregate must be refreshed because crossing this
    /// transition changes whether the row is counted.
    /// </summary>
    public static bool TransitionAffectsAggregate(ReviewState from, ReviewState to) =>
        CountsInAggregate(from) != CountsInAggregate(to);

    /// <summary>True for the single terminal state per FR-005 / FR-005a.</summary>
    public static bool IsTerminal(ReviewState state) => state == ReviewState.Deleted;
}
