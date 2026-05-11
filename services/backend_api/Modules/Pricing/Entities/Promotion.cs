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

    // ---------- spec 007-b US2: bilingual labels (Principle 4) ----------
    // Required at create-time per contract §3 mirror of §2.1. Stored as
    // separate columns (matching Coupon US1 pattern) so PG indexing + admin
    // filtering stays trivial.
    public string LabelAr { get; set; } = string.Empty;
    public string LabelEn { get; set; } = string.Empty;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }

    // ---------- spec 007-b US2: pricing-field columns ----------
    // The 007-a engine reads pricing values from ConfigJson; commercial
    // authoring (007-b) persists the authored values into typed columns AND
    // builds the ConfigJson on save so the engine layer remains unchanged.
    // PercentOff is whole percent (1..100) for the wire shape; engine
    // continues to read percentBps from ConfigJson.
    public int? PercentOff { get; set; }
    public long? AmountOffMinor { get; set; }
    public Guid? RewardSku { get; set; }       // BOGO reward
    public Guid? BundleSku { get; set; }       // bundle target

    // ---------- spec 007-b US2: stacking flags (contract §3) ----------
    // Default true — single-coupon, single-promotion stacking is the
    // common case. Engine treats `false` as a suppression signal.
    public bool StacksWithCoupons { get; set; } = true;
    public bool StacksWithOtherPromotions { get; set; } = true;

    // ---------- spec 007-b US2: author actor (US5 high-impact gate) ----------
    public Guid AuthorActorId { get; set; }

    /// <summary>
    /// Optimistic-concurrency token mapped to PostgreSQL <c>xmin</c>.
    /// </summary>
    public uint XminRowVersion { get; set; }
}
