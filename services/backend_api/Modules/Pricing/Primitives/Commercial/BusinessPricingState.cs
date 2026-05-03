namespace BackendApi.Modules.Pricing.Primitives.Commercial;

/// <summary>
/// Two-state lifecycle for ProductTierPrice rows (spec 007-b §3.2).
/// </summary>
public enum BusinessPricingState
{
    Active = 0,
    Deactivated = 1,
}
