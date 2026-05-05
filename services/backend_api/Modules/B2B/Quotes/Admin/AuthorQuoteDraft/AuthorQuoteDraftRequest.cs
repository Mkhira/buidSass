using System.Text.Json.Serialization;
using BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;

namespace BackendApi.Modules.B2B.Quotes.Admin.AuthorQuoteDraft;

/// <summary>Spec 021 contract §4.3 — author/revise a draft.</summary>
public sealed record AuthorQuoteDraftRequest(
    [property: JsonPropertyName("lines")] IReadOnlyList<AuthorQuoteDraftLine>? Lines,
    [property: JsonPropertyName("terms_text")] LocalizedMessage? TermsText,
    [property: JsonPropertyName("terms_days")] int? TermsDays,
    [property: JsonPropertyName("validity_extends")] bool? ValidityExtends,
    [property: JsonPropertyName("internal_note")] string? InternalNote);

public sealed record AuthorQuoteDraftLine(
    [property: JsonPropertyName("sku")] string? Sku,
    [property: JsonPropertyName("quantity")] int? Quantity,
    [property: JsonPropertyName("override_unit_price")] decimal? OverrideUnitPrice,
    [property: JsonPropertyName("override_reason")] LocalizedMessage? OverrideReason,
    [property: JsonPropertyName("line_discount_amount")] decimal? LineDiscountAmount);

public sealed record AuthorQuoteDraftResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("state")] string State);
