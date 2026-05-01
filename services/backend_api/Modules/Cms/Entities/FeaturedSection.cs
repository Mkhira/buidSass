using BackendApi.Modules.Cms.Storefront;

namespace BackendApi.Modules.Cms.Entities;

/// <summary>Featured section entity per spec 024 data-model §2.2.</summary>
public sealed class FeaturedSection : ICmsContentRow
{
    DateTimeOffset? ICmsContentRow.ScheduledStartUtc => null;
    DateTimeOffset? ICmsContentRow.ScheduledEndUtc => null;

    public Guid Id { get; set; }
    public string SectionKindWire { get; set; } = string.Empty;
    public string? TitleAr { get; set; }
    public string? TitleEn { get; set; }
    public string? SubtitleAr { get; set; }
    public string? SubtitleEn { get; set; }

    /// <summary>jsonb array of <c>{kind: 'product'|'category'|'bundle', id: UUID}</c>.</summary>
    public string ReferencesJson { get; set; } = "[]";

    public int DisplayPriority { get; set; } = 100;
    public string MarketCode { get; set; } = string.Empty;
    public string StateWire { get; set; } = "draft";
    public DateTimeOffset? ScheduledPublishAtUtc { get; set; }
    public Guid? VendorId { get; set; }
    public Guid OwnerActorId { get; set; }
    public bool OwnershipOrphaned { get; set; }
    public DateTimeOffset? LastStaleAlertAtUtc { get; set; }
    public DateTimeOffset? LastStaleAlertDismissedAtUtc { get; set; }
    public DateTimeOffset? LastPartialBrokenAlertAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset EditorSaveAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public DateTimeOffset? ArchivedAtUtc { get; set; }
    public string? ArchiveReasonNote { get; set; }
    public uint Xmin { get; set; }
}
