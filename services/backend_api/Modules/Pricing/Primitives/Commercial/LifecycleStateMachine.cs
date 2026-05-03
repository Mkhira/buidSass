namespace BackendApi.Modules.Pricing.Primitives.Commercial;

/// <summary>
/// Pure transition logic for the <see cref="LifecycleState"/> machine
/// covering every transition row in spec 007-b data-model §3.1.
/// </summary>
public static class LifecycleStateMachine
{
    /// <summary>
    /// Attempt a transition from <paramref name="from"/> via <paramref name="trigger"/>.
    /// On success returns true and sets <paramref name="to"/>; on failure returns false
    /// and sets <paramref name="reasonCode"/> to a stable error code.
    /// </summary>
    public static bool TryTransition(
        LifecycleState from,
        LifecycleTrigger trigger,
        DateTimeOffset nowUtc,
        DateTimeOffset? validFrom,
        DateTimeOffset? validTo,
        out LifecycleState to,
        out string? reasonCode)
    {
        to = from;
        reasonCode = null;

        switch ((from, trigger))
        {
            case (LifecycleState.Draft, LifecycleTrigger.Schedule):
                if (validFrom is null || validTo is null)
                {
                    reasonCode = "commercial.schedule.invalid_window";
                    return false;
                }
                to = validFrom.Value > nowUtc ? LifecycleState.Scheduled : LifecycleState.Active;
                return true;

            case (LifecycleState.Scheduled, LifecycleTrigger.TimerActivate):
                if (validFrom is null || validFrom.Value > nowUtc)
                {
                    reasonCode = "commercial.timer.not_yet_due";
                    return false;
                }
                to = LifecycleState.Active;
                return true;

            case (LifecycleState.Scheduled, LifecycleTrigger.TimerExpire):
            case (LifecycleState.Active, LifecycleTrigger.TimerExpire):
            case (LifecycleState.Deactivated, LifecycleTrigger.TimerExpire):
                if (validTo is null || validTo.Value > nowUtc)
                {
                    reasonCode = "commercial.timer.not_yet_due";
                    return false;
                }
                to = LifecycleState.Expired;
                return true;

            case (LifecycleState.Scheduled, LifecycleTrigger.Deactivate):
            case (LifecycleState.Active, LifecycleTrigger.Deactivate):
            case (LifecycleState.Active, LifecycleTrigger.AutoDeactivateBrokenReferences):
                to = LifecycleState.Deactivated;
                return true;

            case (LifecycleState.Deactivated, LifecycleTrigger.Reactivate):
                if (validTo is not null && validTo.Value <= nowUtc)
                {
                    reasonCode = "commercial.reactivation.expired_terminal";
                    return false;
                }
                to = LifecycleState.Active;
                return true;

            case (LifecycleState.Expired, _):
                reasonCode = "commercial.row.expired_terminal";
                return false;

            default:
                reasonCode = "commercial.row.invalid_transition";
                return false;
        }
    }
}
