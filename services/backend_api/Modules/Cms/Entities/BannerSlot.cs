using BackendApi.Modules.Cms.Storefront;

namespace BackendApi.Modules.Cms.Entities;

/// <summary>Banner slot entity per spec 024 data-model §2.1.</summary>
public sealed class BannerSlot : ICmsContentRow
{
    DateTimeOffset? ICmsContentRow.ScheduledPublishAtUtc => null;

    public Guid Id { get; set; }
    public string SlotKindWire { get; set; } = string.Empty;
    public string? HeadlineAr { get; set; }
    public string? HeadlineEn { get; set; }
    public string? SubheadAr { get; set; }
    public string? SubheadEn { get; set; }
    public Guid? AssetIdAr { get; set; }
    public Guid? AssetIdEn { get; set; }
    public string CtaKindWire { get; set; } = "none";
    public string? CtaTarget { get; set; }
    public string CtaHealthWire { get; set; } = "not_applicable";
    public DateTimeOffset? ScheduledStartUtc { get; set; }
    public DateTimeOffset? ScheduledEndUtc { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public int PriorityWithinSlot { get; set; } = 100;
    public string StateWire { get; set; } = "draft";
    public Guid? VendorId { get; set; }
    public Guid OwnerActorId { get; set; }
    public bool OwnershipOrphaned { get; set; }
    public DateTimeOffset? LastStaleAlertAtUtc { get; set; }
    public DateTimeOffset? LastStaleAlertDismissedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset EditorSaveAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public DateTimeOffset? ArchivedAtUtc { get; set; }
    public string? ArchiveReasonNote { get; set; }
    public uint Xmin { get; set; }
}
