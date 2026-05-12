using System.Text.Json.Serialization;

namespace BackendApi.Modules.Pricing.Admin.Lookups;

/// <summary>
/// Spec 007-b commercial-authoring lookup DTOs (contract §10). These three
/// endpoints exist solely so the spec 015 admin UI does not depend on each
/// upstream module's authorization surface — this contract enforces a single
/// commercial-authoring permission boundary across catalog / b2b / customers
/// pickers.
/// </summary>
public sealed record LookupSkuRow(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("product_id")] Guid? ProductId,
    [property: JsonPropertyName("display_name_ar")] string? DisplayNameAr,
    [property: JsonPropertyName("display_name_en")] string? DisplayNameEn,
    [property: JsonPropertyName("archived")] bool Archived);

public sealed record LookupSkuResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<LookupSkuRow> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public sealed record LookupCompanyRow(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("market_code")] string MarketCode,
    [property: JsonPropertyName("state")] string State);

public sealed record LookupCompanyResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<LookupCompanyRow> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public sealed record LookupSegmentRow(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("market_code")] string MarketCode,
    [property: JsonPropertyName("member_count")] int? MemberCount);

public sealed record LookupSegmentResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<LookupSegmentRow> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);
