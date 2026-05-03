using BackendApi.Modules.Pricing.Primitives.Commercial;

namespace BackendApi.Modules.Pricing.Entities;

public sealed class Coupon
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Kind { get; set; } = "percent";   // percent | amount
    public int Value { get; set; }                   // percent: bps; amount: minor units
    public long? CapMinor { get; set; }
    public int? PerCustomerLimit { get; set; }
    public int? OverallLimit { get; set; }
    public int UsedCount { get; set; }
    public bool ExcludesRestricted { get; set; }
    public string[] MarketCodes { get; set; } = Array.Empty<string>();
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public string? OwnerId { get; set; }
    public Guid? VendorId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // ---------- spec 007-b lifecycle (data-model §2.1) ----------
    public LifecycleState State { get; set; } = LifecycleState.Draft;
    public DateTimeOffset StateChangedAtUtc { get; set; }
    public Guid StateChangedByActorId { get; set; }
    public string? StateChangedReasonNote { get; set; }
    public bool DisplayInBanners { get; set; }
    public bool AppliesToBroken { get; set; }
    public DateTimeOffset? AppliesToBrokenAtUtc { get; set; }

    /// <summary>
    /// Optimistic-concurrency token mapped to PostgreSQL <c>xmin</c>.
    /// EF Core treats this as <c>IsRowVersion()</c> — value populated on read; do not set manually.
    /// </summary>
    public uint XminRowVersion { get; set; }
}
