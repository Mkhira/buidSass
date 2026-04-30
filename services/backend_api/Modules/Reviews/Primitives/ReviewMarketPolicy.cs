namespace BackendApi.Modules.Reviews.Primitives;

/// <summary>
/// Value-object snapshot of a row from <c>reviews.reviews_market_schemas</c>.
/// Resolved at request time and passed through the slice; never mutated.
/// </summary>
public sealed record ReviewMarketPolicy(
    string MarketCode,
    int EligibilityWindowDays,
    int EditWindowDays,
    int CommunityReportThreshold,
    int CommunityReportWindowDays,
    int ReportQualifyingAccountAgeDays,
    bool ReportQualifyingRequiresVerifiedBuyer,
    int PendingModerationSlaHours)
{
    public static ReviewMarketPolicy Default(string marketCode) => new(
        MarketCode: marketCode,
        EligibilityWindowDays: 180,
        EditWindowDays: 30,
        CommunityReportThreshold: 3,
        CommunityReportWindowDays: 30,
        ReportQualifyingAccountAgeDays: 14,
        ReportQualifyingRequiresVerifiedBuyer: true,
        PendingModerationSlaHours: 168);
}
