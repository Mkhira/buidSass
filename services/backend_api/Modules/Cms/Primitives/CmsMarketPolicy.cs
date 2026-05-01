namespace BackendApi.Modules.Cms.Primitives;

/// <summary>
/// Value-object snapshot of a row from <c>cms.market_schemas</c>. Resolved at
/// request time and passed through the slice; never mutated. Per spec 024
/// data-model §2.9.
/// </summary>
public sealed record CmsMarketPolicy(
    string MarketCode,
    int BannerMaxLivePerSlot,
    int FeaturedSectionMaxReferences,
    int PreviewTokenDefaultTtlHours,
    int DraftStalenessAlertDays,
    int AssetGracePeriodDays)
{
    /// <summary>V1 default values per data-model §2.9.</summary>
    public static CmsMarketPolicy Default(string marketCode) => new(
        MarketCode: marketCode,
        BannerMaxLivePerSlot: 5,
        FeaturedSectionMaxReferences: 24,
        PreviewTokenDefaultTtlHours: 24,
        DraftStalenessAlertDays: 30,
        AssetGracePeriodDays: 7);

    public TimeSpan PreviewTokenDefaultTtl => TimeSpan.FromHours(PreviewTokenDefaultTtlHours);
    public TimeSpan DraftStalenessAlertWindow => TimeSpan.FromDays(DraftStalenessAlertDays);
    public TimeSpan AssetGracePeriod => TimeSpan.FromDays(AssetGracePeriodDays);
}
