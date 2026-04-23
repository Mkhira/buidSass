namespace BackendApi.Modules.Search.Customer.LookupBySkuOrBarcode;

public sealed record LookupRequest(
    string Code,
    string? MarketCode,
    string? Locale);

public sealed record LookupResponse(SearchLookupHit? Hit);

public sealed record SearchLookupHit(
    Guid Id,
    string Sku,
    string? Barcode,
    string Name,
    bool Restricted,
    string? RestrictionReasonCode,
    string MarketCode,
    string Locale);
