namespace BackendApi.Modules.Pricing.Primitives.Commercial;

/// <summary>
/// Pure transition logic for the <see cref="BusinessPricingState"/> machine.
/// Spec 007-b data-model §3.2.
/// </summary>
public static class BusinessPricingStateMachine
{
    public static bool TryTransition(
        BusinessPricingState from,
        BusinessPricingTrigger trigger,
        out BusinessPricingState to,
        out string? reasonCode)
    {
        to = from;
        reasonCode = null;

        switch ((from, trigger))
        {
            case (BusinessPricingState.Active, BusinessPricingTrigger.Deactivate):
            case (BusinessPricingState.Active, BusinessPricingTrigger.AutoDeactivateBrokenCompany):
                to = BusinessPricingState.Deactivated;
                return true;

            case (BusinessPricingState.Deactivated, BusinessPricingTrigger.Reactivate):
                to = BusinessPricingState.Active;
                return true;

            default:
                reasonCode = "business_pricing.row.invalid_transition";
                return false;
        }
    }
}
