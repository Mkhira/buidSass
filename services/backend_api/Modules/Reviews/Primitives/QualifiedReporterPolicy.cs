namespace BackendApi.Modules.Reviews.Primitives;

/// <summary>
/// Pure-function evaluator of FR-023's "qualified reporter" predicate. Captured
/// at report time (not at threshold evaluation) per data-model §2.4 / R5 so the
/// outcome is reproducible during dispute audit even if the policy later changes.
/// </summary>
public static class QualifiedReporterPolicy
{
    public sealed record ReporterFacts(int AccountAgeDays, bool HasDeliveredOrder);

    /// <summary>
    /// Returns <see langword="true"/> when the reporter qualifies under <paramref name="policy"/>.
    /// </summary>
    public static bool Evaluate(ReporterFacts facts, ReviewMarketPolicy policy)
    {
        if (facts.AccountAgeDays < policy.ReportQualifyingAccountAgeDays)
        {
            return false;
        }

        if (policy.ReportQualifyingRequiresVerifiedBuyer && !facts.HasDeliveredOrder)
        {
            return false;
        }

        return true;
    }
}
