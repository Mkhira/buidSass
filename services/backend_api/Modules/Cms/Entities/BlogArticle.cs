using BackendApi.Modules.Cms.Storefront;

namespace BackendApi.Modules.Cms.Entities;

/// <summary>Blog article entity per spec 024 data-model §2.4.</summary>
public sealed class BlogArticle : ICmsContentRow
{
    DateTimeOffset? ICmsContentRow.ScheduledStartUtc => null;
    DateTimeOffset? ICmsContentRow.ScheduledEndUtc => null;

    public Guid Id { get; set; }
    public string CategoryWire { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string AuthoredLocale { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Body { get; set; }
    public Guid? CoverAssetId { get; set; }
    public string? SeoMetaTitle { get; set; }
    public string? SeoMetaDescription { get; set; }
    public Guid? SeoOgImageId { get; set; }
    public string SeoSchemaOrgKind { get; set; } = "BlogPosting";
    public DateTimeOffset? ScheduledPublishAtUtc { get; set; }
    public string MarketCode { get; set; } = string.Empty;
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
