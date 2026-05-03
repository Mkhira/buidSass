namespace BackendApi.Modules.Pricing.Primitives.Commercial;

/// <summary>
/// Snapshot of a draft Coupon or Promotion against which the high-impact gate
/// is evaluated (FR-025). Pure data; no DB access.
/// </summary>
public sealed record HighImpactCandidate(
    decimal? PercentOff,
    long? AmountOffMinor,
    long? CapMinor,
    int? PerCustomerLimit,
    int? OverallLimit,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo);
