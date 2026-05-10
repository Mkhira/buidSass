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

    // ---------- bilingual operator labels (spec 007-b US1) ----------
    // Required at create-time per contract §2.1 / acceptance scenario 4
    // (`coupon.label.required_bilingual`). Stored as separate columns rather
    // than a JSONB blob to keep PG indexing and admin filtering trivial.
    public string LabelAr { get; set; } = string.Empty;
    public string LabelEn { get; set; } = string.Empty;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }

    // ---------- amount_off persistence (spec 007-b US1) ----------
    // The existing 007-a `Value` column is `int` (bps for percent kind). Amount-off
    // coupons need their own minor-units column with a `bigint` shape; reusing the
    // legacy `Value` would clamp at int.MaxValue (~ SAR 21M ≈ acceptable today,
    // but the engine reads coupon kind from `Kind` and values from `Value`, so
    // either column would work). We add a separate column to make the intent
    // explicit and to avoid silent overflow on future high-cap coupons.
    public long? AmountOffMinor { get; set; }

    // ---------- stacking flag (spec 007-b US1, contract §2.1) ----------
    // True (default per spec clarification Q3) means a Coupon may stack with
    // a Promotion in the same cart; the engine reads this when both layers
    // resolve. The companion `stacks_with_other_coupons` flag is hard-enforced
    // at value=false by 007-a (one coupon per cart) and is not authored here.
    public bool StacksWithPromotions { get; set; } = true;

    // ---------- author actor (spec 007-b US5 high-impact gate) ----------
    // Tracked from create-time so the approval flow can assert the
    // self-approval guard (R12 layer 1) without joining audit history.
    public Guid AuthorActorId { get; set; }

    /// <summary>
    /// Optimistic-concurrency token mapped to PostgreSQL <c>xmin</c>.
    /// EF Core treats this as <c>IsRowVersion()</c> — value populated on read; do not set manually.
    /// </summary>
    public uint XminRowVersion { get; set; }
}
