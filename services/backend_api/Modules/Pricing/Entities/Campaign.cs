using BackendApi.Modules.Pricing.Primitives.Commercial;

namespace BackendApi.Modules.Pricing.Entities;

/// <summary>
/// Spec 007-b campaign — bilingual merchandising container linked to a Promotion or Coupon.
/// Data-model §2.4.
/// </summary>
public sealed class Campaign
{
    public Guid Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidTo { get; set; }
    public string[] Markets { get; set; } = Array.Empty<string>();
    public string? LandingQuery { get; set; }
    public string? NotesInternal { get; set; }
    public LifecycleState State { get; set; } = LifecycleState.Draft;
    public DateTimeOffset StateChangedAtUtc { get; set; }
    public Guid StateChangedByActorId { get; set; }
    public string? StateChangedReasonNote { get; set; }
    public bool LinkBroken { get; set; }
    public Guid? VendorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint XminRowVersion { get; set; }
}
