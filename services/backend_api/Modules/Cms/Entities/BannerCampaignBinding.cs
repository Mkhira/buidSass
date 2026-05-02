namespace BackendApi.Modules.Cms.Entities;

/// <summary>Append-only banner ↔ campaign binding row per spec 024 data-model §2.8.</summary>
public sealed class BannerCampaignBinding
{
    public Guid Id { get; set; }
    public Guid BannerId { get; set; }
    public Guid VersionId { get; set; }
    public Guid CampaignId { get; set; }
    public DateTimeOffset BoundAtUtc { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
    public string BindingStateWire { get; set; } = "active";
    public Guid? ReleaseActorId { get; set; }
    public string? ReleaseReasonNote { get; set; }
    public uint Xmin { get; set; }
}
