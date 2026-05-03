namespace BackendApi.Modules.Pricing.Primitives.Commercial;

/// <summary>
/// Five-state lifecycle for Coupon, Promotion, and Campaign rows (spec 007-b §3.1).
/// </summary>
public enum LifecycleState
{
    Draft = 0,
    Scheduled = 1,
    Active = 2,
    Deactivated = 3,
    Expired = 4,
}
