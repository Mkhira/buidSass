using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromProduct;

/// <summary>
/// Spec 021 contract §2.2 — 201 success response. Same wire shape as
/// <c>RequestQuoteFromCartResponse</c> intentionally: the storefront (spec 014)
/// navigates to the same "quote requested" detail screen regardless of whether the
/// quote originated from cart-snapshot or single-product intake.
/// </summary>
public sealed record RequestQuoteFromProductResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("market_code")] string MarketCode,
    [property: JsonPropertyName("company_id")] Guid? CompanyId,
    [property: JsonPropertyName("branch_id")] Guid? BranchId,
    [property: JsonPropertyName("originating_product_id")] Guid OriginatingProductId,
    [property: JsonPropertyName("requested_at")] DateTimeOffset RequestedAt);
