namespace BackendApi.Modules.Shipping.Domain;

/// <summary>
/// Catalog-of-methods row per <c>data-model.md</c> §shipping_methods.
/// Owns the cross-version identity; lifecycle/snapshot details live on
/// <see cref="ShippingMethodVersion"/>.
/// </summary>
public sealed class ShippingMethod
{
    public Guid Id { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }

    /// <summary>FK to the currently-active version; null until first publish.</summary>
    public Guid? CurrentVersionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
