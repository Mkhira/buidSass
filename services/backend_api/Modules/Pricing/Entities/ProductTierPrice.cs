using BackendApi.Modules.Pricing.Primitives.Commercial;

namespace BackendApi.Modules.Pricing.Entities;

public sealed class ProductTierPrice
{
    public Guid ProductId { get; set; }
    public Guid TierId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public long NetMinor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // ---------- spec 007-b business-pricing additive columns (data-model §2.3) ----------
    // The full surrogate-PK + XOR reshape is deferred to Phase 5 (US3) when the
    // BusinessPricing authoring slices land; this commit ships the additive
    // columns only so the lifecycle / vendor / row_version surface is in place.
    public Guid? CompanyId { get; set; }
    public Guid? CopiedFromTierId { get; set; }
    public BusinessPricingState State { get; set; } = BusinessPricingState.Active;
    public DateTimeOffset StateChangedAtUtc { get; set; }
    public Guid StateChangedByActorId { get; set; }
    public string? StateChangedReasonNote { get; set; }
    public bool CompanyLinkBroken { get; set; }
    public DateTimeOffset? CompanyLinkBrokenAtUtc { get; set; }
    public Guid? VendorId { get; set; }
    public uint XminRowVersion { get; set; }
}
