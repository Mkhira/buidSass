namespace BackendApi.Modules.Cms.Entities;

/// <summary>Per-market policy row per spec 024 data-model §2.9.</summary>
public sealed class CmsMarketSchema
{
    public string MarketCode { get; set; } = string.Empty;
    public int BannerMaxLivePerSlot { get; set; } = 5;
    public int FeaturedSectionMaxReferences { get; set; } = 24;
    public int PreviewTokenDefaultTtlHours { get; set; } = 24;
    public int DraftStalenessAlertDays { get; set; } = 30;
    public int AssetGracePeriodDays { get; set; } = 7;
    public Guid LastEditedByActorId { get; set; }
    public DateTimeOffset LastEditedAtUtc { get; set; }
    public uint Xmin { get; set; }
}
