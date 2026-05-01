namespace BackendApi.Modules.Cms.Entities;

/// <summary>CMS asset metadata entity per spec 024 data-model §2.6.</summary>
public sealed class CmsAsset
{
    public Guid Id { get; set; }
    public Guid StorageObjectId { get; set; }
    public string Mime { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? IntendedLocale { get; set; }
    public string OriginalFilename { get; set; } = string.Empty;
    public string StorageObjectStateWire { get; set; } = "active";
    public DateTimeOffset? DereferencedAtUtc { get; set; }
    public DateTimeOffset? SweptAtUtc { get; set; }
    public Guid UploadedByActorId { get; set; }
    public DateTimeOffset UploadedAtUtc { get; set; }
    public uint Xmin { get; set; }
}
