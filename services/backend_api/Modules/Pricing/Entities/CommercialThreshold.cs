namespace BackendApi.Modules.Pricing.Entities;

/// <summary>
/// Per-market high-impact-gate threshold row. Spec 007-b data-model §2.8.
/// PK: <see cref="MarketCode"/>. Seeded by <c>PricingThresholdsSeeder</c>.
/// </summary>
public sealed class CommercialThreshold
{
    public string MarketCode { get; set; } = string.Empty;
    public bool GateEnabled { get; set; } = true;
    public decimal? ThresholdPercentOff { get; set; }
    public long? ThresholdAmountOffMinor { get; set; }
    public int? ThresholdDurationDays { get; set; }
    public int CouponInFlightGraceSeconds { get; set; } = 1800;
    public int PromotionInFlightGraceSeconds { get; set; } = 1800;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Guid UpdatedByActorId { get; set; }
    public uint XminRowVersion { get; set; }
}
