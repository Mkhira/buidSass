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

    /// <summary>
    /// Per-ADR-010 multi-tenant market discriminator. Required for the asset
    /// GC worker's per-market grace-window resolution and for residency
    /// compliance when spec 015 ships uploads. Allowed values: 'EG', 'KSA', '*'.
    /// </summary>
    public string MarketCode { get; set; } = "*";

    public string StorageObjectStateWire { get; set; } = "active";
    public DateTimeOffset? DereferencedAtUtc { get; set; }
    public DateTimeOffset? SweptAtUtc { get; set; }
    public Guid UploadedByActorId { get; set; }
    public DateTimeOffset UploadedAtUtc { get; set; }
    public uint Xmin { get; set; }
}
