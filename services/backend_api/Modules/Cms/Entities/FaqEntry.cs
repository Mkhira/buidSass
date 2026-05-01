using BackendApi.Modules.Cms.Storefront;

namespace BackendApi.Modules.Cms.Entities;

/// <summary>FAQ entry entity per spec 024 data-model §2.3.</summary>
public sealed class FaqEntry : ICmsContentRow
{
    DateTimeOffset? ICmsContentRow.ScheduledStartUtc => null;
    DateTimeOffset? ICmsContentRow.ScheduledEndUtc => null;

    public Guid Id { get; set; }
    public string CategoryWire { get; set; } = string.Empty;
    public string? QuestionAr { get; set; }
    public string? QuestionEn { get; set; }
    public string? AnswerAr { get; set; }
    public string? AnswerEn { get; set; }
    public int DisplayOrder { get; set; } = 100;
    public string MarketCode { get; set; } = string.Empty;
    public string StateWire { get; set; } = "draft";
    public DateTimeOffset? ScheduledPublishAtUtc { get; set; }
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
