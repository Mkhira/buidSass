namespace BackendApi.Modules.Search.Customer.SearchProducts;

public sealed record SearchProductsRequest(
    string? Query,
    string? MarketCode,
    string? Locale,
    SearchProductsFilters? Filters,
    string? Sort,
    int? Page,
    int? PageSize);

public sealed record SearchProductsFilters(
    string[]? BrandIds,
    string[]? CategoryIds,
    long? PriceMinMinor,
    long? PriceMaxMinor,
    string? Restricted,
    string? Availability);

public sealed record SearchProductsResponse(
    IReadOnlyList<SearchProductHit> Hits,
    SearchFacets Facets,
    int TotalEstimate,
    int QueryDurationMs,
    int EngineLatencyMs,
    bool LocaleFallbackApplied);

public sealed record SearchProductHit(
    Guid Id,
    string Sku,
    string? Barcode,
    string Name,
    string? ShortDescription,
    Guid BrandId,
    string BrandName,
    IReadOnlyList<Guid> CategoryIds,
    long? PriceHintMinorUnits,
    bool Restricted,
    string? RestrictionReasonCode,
    string Availability,
    string? ThumbUrl,
    string MarketCode,
    string Locale);

public sealed record SearchFacets(
    IReadOnlyDictionary<string, int> BrandId,
    IReadOnlyDictionary<string, int> CategoryId,
    IReadOnlyDictionary<string, int> PriceBucket,
    IReadOnlyDictionary<string, int> Restricted,
    IReadOnlyDictionary<string, int> Availability);
