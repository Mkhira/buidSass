namespace BackendApi.Modules.Pricing.Primitives.Commercial;

/// <summary>
/// Triggers for the <see cref="BusinessPricingState"/> machine (spec 007-b §3.2).
/// </summary>
public enum BusinessPricingTrigger
{
    Deactivate,
    Reactivate,
    AutoDeactivateBrokenCompany,
}
