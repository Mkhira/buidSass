using BackendApi.Modules.Pricing.Primitives.Commercial;

namespace BackendApi.Modules.Pricing.Entities;

/// <summary>
/// Represents a tier-row OR company-override row on a product (data-model §2.3).
/// Three valid shapes enforced by the <c>chk_tier_xor_company</c> check constraint:
/// <list type="bullet">
///   <item><c>tier_id</c> set, <c>company_id</c> null — classic 007-a tier row.</item>
///   <item><c>tier_id</c> null, <c>company_id</c> set — company override standalone.</item>
///   <item>Both set with <c>copied_from_tier_id</c> set — company override copied from a tier row (audit pointer).</item>
/// </list>
/// The 007-a engine continues to resolve tier-only rows by
/// <c>(tier_id, market_code, product_id)</c>; the company-override resolution
/// path is added by US3.
/// </summary>
public sealed class ProductTierPrice
{
    /// <summary>
    /// Surrogate primary key introduced by spec 007-b US3. The legacy
    /// composite-key shape <c>(product_id, tier_id, market_code)</c> is
    /// preserved via a unique partial index so the 007-a engine query
    /// continues to work unchanged.
    /// </summary>
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid? TierId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public long NetMinor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // ---------- spec 007-b business-pricing columns (data-model §2.3) ----------
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
