namespace BackendApi.Modules.Pricing.Primitives.Commercial;

/// <summary>
/// Snapshot of a draft Coupon or Promotion against which the high-impact gate
/// is evaluated (FR-025). Pure data; no DB access.
/// </summary>
/// <remarks>
/// <para>
/// <c>UsageLimitsApplicable</c> controls whether Criterion 3 ("both usage limits
/// unset → high-impact") is evaluated. Coupons carry usage limits as authoring
/// fields, so they pass <c>true</c> and a draft with both limits null is treated
/// as high-impact. Promotions are not gated on usage limits (the concept does
/// not apply at launch — see spec 007-b §FR-025 / data-model §3.1), so they
/// pass <c>false</c> and Criterion 3 short-circuits to a no-trip.
/// </para>
/// <para>
/// Default <c>true</c> preserves US1 coupon behaviour (records constructed
/// positionally before US2 still trigger Criterion 3).
/// </para>
/// </remarks>
public sealed record HighImpactCandidate(
    decimal? PercentOff,
    long? AmountOffMinor,
    long? CapMinor,
    int? PerCustomerLimit,
    int? OverallLimit,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo)
{
    public bool UsageLimitsApplicable { get; init; } = true;
}
