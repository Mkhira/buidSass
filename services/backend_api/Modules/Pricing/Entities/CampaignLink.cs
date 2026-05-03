namespace BackendApi.Modules.Pricing.Entities;

/// <summary>
/// Spec 007-b campaign-link — typed association between a Campaign and a Promotion / Coupon /
/// landing-only target. Data-model §2.5.
/// </summary>
public sealed class CampaignLink
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public string Kind { get; set; } = "landing_only"; // promotion | coupon | landing_only
    public Guid? TargetId { get; set; }
    public DateTimeOffset? LinkBrokenAtUtc { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
