using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromProduct;

/// <summary>
/// Spec 021 contract §2.2 — 201 success response for the from-product intake.
/// Same shape as <see cref="RequestQuoteFromCart.RequestQuoteFromCartResponse"/> with
/// an additional <see cref="OriginatingProductId"/> echo so the storefront can
/// confirm which product the quote was minted from.
/// </summary>
public sealed record RequestQuoteFromProductResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("market_code")] string MarketCode,
    [property: JsonPropertyName("company_id")] Guid? CompanyId,
    [property: JsonPropertyName("branch_id")] Guid? BranchId,
    [property: JsonPropertyName("originating_product_id")] Guid OriginatingProductId,
    [property: JsonPropertyName("requested_at")] DateTimeOffset RequestedAt);
