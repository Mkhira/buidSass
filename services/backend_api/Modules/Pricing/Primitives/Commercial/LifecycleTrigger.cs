namespace BackendApi.Modules.Pricing.Primitives.Commercial;

/// <summary>
/// Triggers that drive <see cref="LifecycleState"/> transitions (spec 007-b §3.1).
/// </summary>
public enum LifecycleTrigger
{
    Schedule,
    TimerActivate,
    TimerExpire,
    Deactivate,
    Reactivate,
    AutoDeactivateBrokenReferences,
}
