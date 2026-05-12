using System.Text.Json.Serialization;

namespace BackendApi.Modules.Pricing.Admin.Thresholds;

/// <summary>
/// Spec 007-b commercial-threshold DTOs (US5 / contract §9). super_admin-only
/// per-market knobs that tune the high-impact-gate behaviour (FR-025).
/// </summary>
public sealed record CommercialThresholdResponse(
    [property: JsonPropertyName("market_code")] string MarketCode,
    [property: JsonPropertyName("gate_enabled")] bool GateEnabled,
    [property: JsonPropertyName("threshold_percent_off")] decimal? ThresholdPercentOff,
    [property: JsonPropertyName("threshold_amount_off_minor")] long? ThresholdAmountOffMinor,
    [property: JsonPropertyName("threshold_duration_days")] int? ThresholdDurationDays,
    [property: JsonPropertyName("coupon_in_flight_grace_seconds")] int CouponInFlightGraceSeconds,
    [property: JsonPropertyName("promotion_in_flight_grace_seconds")] int PromotionInFlightGraceSeconds,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("updated_by_actor_id")] Guid UpdatedByActorId,
    [property: JsonPropertyName("row_version")] uint RowVersion);

/// <summary>
/// PATCH shape. Sentinel semantics: the caller MUST send the field they want
/// to change. We model "null = disable that criterion" via the
/// <see cref="CommercialThresholdPatch"/>-shaped payload: a wrapper that
/// distinguishes "absent" (don't touch) from "present with null value"
/// (disable). Since System.Text.Json cannot natively distinguish, we use a
/// JsonElement-bearing helper at the endpoint layer.
/// </summary>
public sealed record UpdateCommercialThresholdRequest(
    [property: JsonPropertyName("gate_enabled")] bool? GateEnabled,
    [property: JsonPropertyName("threshold_percent_off")] decimal? ThresholdPercentOff,
    [property: JsonPropertyName("threshold_amount_off_minor")] long? ThresholdAmountOffMinor,
    [property: JsonPropertyName("threshold_duration_days")] int? ThresholdDurationDays,
    [property: JsonPropertyName("coupon_in_flight_grace_seconds")] int? CouponInFlightGraceSeconds,
    [property: JsonPropertyName("promotion_in_flight_grace_seconds")] int? PromotionInFlightGraceSeconds);
