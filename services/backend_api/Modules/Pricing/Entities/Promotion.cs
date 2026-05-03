using BackendApi.Modules.Pricing.Primitives.Commercial;

namespace BackendApi.Modules.Pricing.Entities;

public sealed class Promotion
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "percent_off";
    public string Name { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public Guid[]? AppliesToProductIds { get; set; }
    public Guid[]? AppliesToCategoryIds { get; set; }
    public string[] MarketCodes { get; set; } = Array.Empty<string>();
    public int Priority { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string? OwnerId { get; set; }
    public Guid? VendorId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // ---------- spec 007-b lifecycle (data-model §2.2) ----------
    public LifecycleState State { get; set; } = LifecycleState.Draft;
    public DateTimeOffset StateChangedAtUtc { get; set; }
    public Guid StateChangedByActorId { get; set; }
    public string? StateChangedReasonNote { get; set; }
    public bool BannerEligible { get; set; }
    public bool AppliesToBroken { get; set; }
    public DateTimeOffset? AppliesToBrokenAtUtc { get; set; }

    /// <summary>
    /// Optimistic-concurrency token mapped to PostgreSQL <c>xmin</c>.
    /// </summary>
    public uint XminRowVersion { get; set; }
}
