using BackendApi.Modules.Cms.Storefront;

namespace BackendApi.Modules.Cms.Entities;

/// <summary>Legal page version entity per spec 024 data-model §2.5. Append-only.</summary>
public sealed class LegalPageVersion : ICmsContentRow
{
    DateTimeOffset? ICmsContentRow.ScheduledStartUtc => EffectiveAtUtc;
    DateTimeOffset? ICmsContentRow.ScheduledEndUtc => null;
    DateTimeOffset? ICmsContentRow.ScheduledPublishAtUtc => EffectiveAtUtc;

    public Guid Id { get; set; }
    public string LegalPageKindWire { get; set; } = string.Empty;
    public string VersionLabel { get; set; } = string.Empty;
    public string? BodyAr { get; set; }
    public string? BodyEn { get; set; }
    public DateTimeOffset? EffectiveAtUtc { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public string StateWire { get; set; } = "draft";
    public DateTimeOffset? SupersededAtUtc { get; set; }
    public Guid? SupersededByVersionId { get; set; }
    public Guid? VendorId { get; set; }
    public Guid OwnerActorId { get; set; }
    public bool OwnershipOrphaned { get; set; }
    public DateTimeOffset? LastStaleAlertAtUtc { get; set; }
    public DateTimeOffset? LastStaleAlertDismissedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset EditorSaveAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public uint Xmin { get; set; }
}
