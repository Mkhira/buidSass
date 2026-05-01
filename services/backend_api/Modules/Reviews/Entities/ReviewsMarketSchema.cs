namespace BackendApi.Modules.Reviews.Entities;

/// <summary>
/// Per-market policy row per data-model §2.7. Every market-tunable knob lives
/// here per Principle 5 — no hardcoded EG/KSA branches anywhere in the module.
/// </summary>
public sealed class ReviewsMarketSchema
{
    public string MarketCode { get; set; } = string.Empty;

    public int EligibilityWindowDays { get; set; }
    public int EditWindowDays { get; set; }
    public int CommunityReportThreshold { get; set; }
    public int CommunityReportWindowDays { get; set; }
    public int ReportQualifyingAccountAgeDays { get; set; }
    public bool ReportQualifyingRequiresVerifiedBuyer { get; set; }
    public int PendingModerationSlaHours { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Guid UpdatedByActorId { get; set; }

    public uint Xmin { get; set; }
}
