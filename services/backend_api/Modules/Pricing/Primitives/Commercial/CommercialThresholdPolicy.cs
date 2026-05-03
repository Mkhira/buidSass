namespace BackendApi.Modules.Pricing.Primitives.Commercial;

/// <summary>
/// Per-market threshold policy for the high-impact approval gate (FR-025).
/// Loaded from <c>pricing.commercial_thresholds</c>; null criterion fields
/// disable that single criterion (not the entire gate).
/// </summary>
/// <remarks>
/// Disabling the entire gate requires <see cref="GateEnabled"/>=false, which is
/// a separate per-market flag (super_admin only, audited).
/// </remarks>
public sealed record CommercialThresholdPolicy(
    string MarketCode,
    bool GateEnabled,
    decimal? ThresholdPercentOff,
    long? ThresholdAmountOffMinor,
    int? ThresholdDurationDays,
    int CouponInFlightGraceSeconds,
    int PromotionInFlightGraceSeconds);
