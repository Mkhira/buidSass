namespace BackendApi.Modules.Pricing.Primitives.Commercial;

/// <summary>
/// Pure FR-025 gate logic. Returns true when any of the four configured
/// criteria fires for the candidate against the policy. Returns false if
/// the gate is disabled (<see cref="CommercialThresholdPolicy.GateEnabled"/>).
/// </summary>
public static class HighImpactGate
{
    public static bool IsTriggered(HighImpactCandidate candidate, CommercialThresholdPolicy policy)
    {
        if (!policy.GateEnabled)
        {
            return false;
        }

        // Criterion 1: percent off ≥ threshold (only when policy threshold is set).
        if (policy.ThresholdPercentOff is { } pctThreshold &&
            candidate.PercentOff is { } pct &&
            pct >= pctThreshold)
        {
            return true;
        }

        // Criterion 2: cap or fixed amount ≥ threshold.
        if (policy.ThresholdAmountOffMinor is { } amountThreshold)
        {
            var rulingAmount = candidate.CapMinor ?? candidate.AmountOffMinor;
            if (rulingAmount is { } amt && amt >= amountThreshold)
            {
                return true;
            }
        }

        // Criterion 3: both usage limits unset.
        if (candidate.PerCustomerLimit is null && candidate.OverallLimit is null)
        {
            return true;
        }

        // Criterion 4: schedule duration ≥ threshold.
        if (policy.ThresholdDurationDays is { } daysThreshold &&
            candidate.ValidFrom is { } from &&
            candidate.ValidTo is { } to)
        {
            var durationDays = (to - from).TotalDays;
            if (durationDays >= daysThreshold)
            {
                return true;
            }
        }

        return false;
    }
}
