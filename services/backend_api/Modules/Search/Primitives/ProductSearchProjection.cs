using System.Text.Json;
using System.Text.Json.Serialization;
using BackendApi.Modules.Search.Primitives.Normalization;

namespace BackendApi.Modules.Search.Primitives;

public sealed record ProductPublishedEvent(
    Guid Id,
    string Sku,
    string[]? MarketCodes,
    bool Restricted);

public sealed record CatalogProductSnapshot(
    Guid Id,
    string Sku,
    string? Barcode,
    string NameAr,
    string NameEn,
    string? ShortDescriptionAr,
    string? ShortDescriptionEn,
    Guid BrandId,
    string BrandNameAr,
    string BrandNameEn,
    IReadOnlyList<Guid> CategoryIds,
    IReadOnlyList<string> CategoryNamesAr,
    IReadOnlyList<string> CategoryNamesEn,
    string AttributesJson,
    long? PriceHintMinorUnits,
    bool Restricted,
    string? RestrictionReasonCode,
    string[] MarketCodes,
    Guid? VendorId,
    DateTimeOffset? PublishedAt,
    string? PrimaryMediaThumbUrl,
    string? PrimaryMediaCardUrl);

public sealed record ProductSearchProjection
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("sku")]
    public string Sku { get; init; } = string.Empty;

    [JsonPropertyName("barcode")]
    public string? Barcode { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("nameNormalized")]
    public string NameNormalized { get; init; } = string.Empty;

    [JsonPropertyName("shortDescription")]
    public string? ShortDescription { get; init; }

    [JsonPropertyName("brandId")]
    public Guid BrandId { get; init; }

    [JsonPropertyName("brandName")]
    public string BrandName { get; init; } = string.Empty;

    [JsonPropertyName("categoryIds")]
    public IReadOnlyList<Guid> CategoryIds { get; init; } = [];

    [JsonPropertyName("categoryBreadcrumb")]
    public IReadOnlyList<string> CategoryBreadcrumb { get; init; } = [];

    [JsonPropertyName("attributes")]
    public Dictionary<string, object?> Attributes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("priceHintMinorUnits")]
    public long? PriceHintMinorUnits { get; init; }

    [JsonPropertyName("priceBucket")]
    public string? PriceBucket { get; init; }

    [JsonPropertyName("restricted")]
    public bool Restricted { get; init; }

    [JsonPropertyName("restrictionReasonCode")]
    public string? RestrictionReasonCode { get; init; }

    [JsonPropertyName("availability")]
    public string Availability { get; init; } = "in_stock";

    [JsonPropertyName("featuredAt")]
    public DateTimeOffset? FeaturedAt { get; init; }

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt { get; init; }

    [JsonPropertyName("primaryMedia")]
    public SearchPrimaryMedia PrimaryMedia { get; init; } = new(null, null);

    [JsonPropertyName("marketCode")]
    public string MarketCode { get; init; } = string.Empty;

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = string.Empty;

    [JsonPropertyName("vendorId")]
    public Guid? VendorId { get; init; }
}

public sealed record SearchPrimaryMedia(
    [property: JsonPropertyName("thumbUrl")] string? ThumbUrl,
    [property: JsonPropertyName("cardUrl")] string? CardUrl);

public static class ProductSearchProjectionMapper
{
    public static ProductPublishedEvent ParsePublishedEvent(string payloadJson)
    {
        return JsonSerializer.Deserialize<ProductPublishedEvent>(payloadJson)
            ?? throw new InvalidOperationException("Invalid catalog.product.published payload.");
    }

    public static ProductSearchProjection FromCatalogProduct(
        CatalogProductSnapshot snapshot,
        string locale,
        string marketCode,
        ArabicNormalizer normalizer)
    {
        var isArabic = string.Equals(locale, "ar", StringComparison.OrdinalIgnoreCase);
        var name = isArabic ? snapshot.NameAr : snapshot.NameEn;
        var shortDescription = isArabic ? snapshot.ShortDescriptionAr : snapshot.ShortDescriptionEn;
        var brandName = isArabic ? snapshot.BrandNameAr : snapshot.BrandNameEn;
        var categoryBreadcrumb = isArabic ? snapshot.CategoryNamesAr : snapshot.CategoryNamesEn;

        Dictionary<string, object?> attributes;
        try
        {
            attributes = JsonSerializer.Deserialize<Dictionary<string, object?>>(snapshot.AttributesJson)
                ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        return new ProductSearchProjection
        {
            Id = snapshot.Id,
            Sku = snapshot.Sku,
            Barcode = snapshot.Barcode,
            Name = name,
            NameNormalized = normalizer.Normalize(name),
            ShortDescription = shortDescription,
            BrandId = snapshot.BrandId,
            BrandName = brandName,
            CategoryIds = snapshot.CategoryIds,
            CategoryBreadcrumb = categoryBreadcrumb,
            Attributes = attributes,
            PriceHintMinorUnits = snapshot.PriceHintMinorUnits,
            PriceBucket = ToPriceBucket(snapshot.PriceHintMinorUnits),
            Restricted = snapshot.Restricted,
            RestrictionReasonCode = snapshot.RestrictionReasonCode,
            Availability = "in_stock",
            PublishedAt = snapshot.PublishedAt,
            PrimaryMedia = new SearchPrimaryMedia(snapshot.PrimaryMediaThumbUrl, snapshot.PrimaryMediaCardUrl),
            MarketCode = marketCode,
            Locale = locale,
            VendorId = snapshot.VendorId,
        };
    }

    public static string? ToPriceBucket(long? minorUnits) => minorUnits switch
    {
        null => null,
        < 10_000 => "0-99",
        < 50_000 => "100-499",
        < 200_000 => "500-1999",
        _ => "2000+",
    };
}
