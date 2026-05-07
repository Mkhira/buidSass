namespace BackendApi.Modules.Support.Primitives;

/// <summary>
/// Spec 023 T102 · research.md §R-09 — pure window + cap math for the
/// customer-initiated reopen path. Extracted from
/// <c>ReopenTicketHandler</c> so it can be unit-tested table-driven
/// without spinning a Postgres / DbContext fixture.
/// </summary>
public static class ReopenWindowMath
{
    public enum ReopenEligibility
    {
        Eligible,
        DisabledForMarket,
        ResolvedAtMissing,
        WindowClosed,
        CountExceeded,
    }

    /// <summary>
    /// Computes the per-market reopen window-end timestamp from the
    /// resolved-at instant. Caller must already have validated that the
    /// ticket is in <c>resolved</c> state and that the policy is enabled.
    /// </summary>
    public static DateTimeOffset ComputeWindowEndUtc(
        DateTimeOffset resolvedAtUtc,
        int reopenWindowDays)
    {
        if (reopenWindowDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reopenWindowDays),
                "reopen_window_days must be ≥ 0.");
        }
        return resolvedAtUtc.AddDays(reopenWindowDays);
    }

    /// <summary>
    /// Pure eligibility check — returns the precise eligibility verdict for
    /// the given (resolvedAt, reopenCount, policy) tuple at <paramref name="nowUtc"/>.
    /// Maps 1:1 to the reason-code surface in <see cref="TicketReasonCode"/>.
    /// </summary>
    public static ReopenEligibility EvaluateEligibility(
        DateTimeOffset nowUtc,
        DateTimeOffset? resolvedAtUtc,
        int reopenCount,
        int reopenWindowDays,
        int maxReopenCount,
        bool reopenEnabled)
    {
        if (!reopenEnabled || reopenWindowDays <= 0 || maxReopenCount <= 0)
        {
            return ReopenEligibility.DisabledForMarket;
        }

        if (resolvedAtUtc is null)
        {
            return ReopenEligibility.ResolvedAtMissing;
        }

        if (reopenCount >= maxReopenCount)
        {
            return ReopenEligibility.CountExceeded;
        }

        var windowEnd = ComputeWindowEndUtc(resolvedAtUtc.Value, reopenWindowDays);
        if (nowUtc > windowEnd)
        {
            return ReopenEligibility.WindowClosed;
        }

        return ReopenEligibility.Eligible;
    }

    /// <summary>
    /// Maps an eligibility verdict to its <see cref="TicketReasonCode"/>
    /// wire value (for the ineligible verdicts). Returns <c>null</c> for
    /// <see cref="ReopenEligibility.Eligible"/>.
    /// </summary>
    public static string? ToReasonCode(ReopenEligibility eligibility) => eligibility switch
    {
        ReopenEligibility.Eligible => null,
        ReopenEligibility.DisabledForMarket => TicketReasonCode.ReopenDisabledForMarket,
        ReopenEligibility.ResolvedAtMissing => TicketReasonCode.ReopenWindowClosed,
        ReopenEligibility.WindowClosed => TicketReasonCode.ReopenWindowClosed,
        ReopenEligibility.CountExceeded => TicketReasonCode.ReopenCountExceeded,
        _ => TicketReasonCode.InvalidTransition,
    };
}
