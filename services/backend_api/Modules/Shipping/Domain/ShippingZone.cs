namespace BackendApi.Modules.Shipping.Domain;

/// <summary>
/// Per-market shipping zone per <c>data-model.md</c> §shipping_zones.
/// Zone resolution: city-list first, postal-code-prefix tie-breaker (research §1).
/// </summary>
public sealed class ShippingZone
{
    public Guid Id { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;

    /// <summary>Array of postal-code prefixes (JSON array of strings).</summary>
    public string PostalCodePrefixesJson { get; set; } = "[]";

    /// <summary>Array of city slugs (JSON array of strings).</summary>
    public string CityListJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
