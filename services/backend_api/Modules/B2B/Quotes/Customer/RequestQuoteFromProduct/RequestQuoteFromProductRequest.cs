using System.Text.Json.Serialization;
using BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromProduct;

/// <summary>
/// Spec 021 contract §2.2 — request DTO for
/// <c>POST /api/customer/quotes/from-product</c>. Wire shape (snake_case JSON):
/// <code>
/// {
///   "product_id": "01HX...",
///   "quantity":   5,
///   "company_id": "01HX...",        // optional — for company-buyer flow
///   "branch_id":  "01HX...",        // optional — only meaningful with company_id
///   "po_number":  "PO-2026-0042",   // optional
///   "message":    { "en": "...",    // optional — at least one locale when present
///                   "ar": "..." }
/// }
/// </code>
/// Reuses <see cref="LocalizedMessage"/> from the from-cart slice — both surfaces share
/// the same Principle 4 bilingual envelope. Cart is NOT cleared on this path
/// (contract §2.2 — the customer may legitimately have other items in their cart).
/// </summary>
public sealed record RequestQuoteFromProductRequest(
    [property: JsonPropertyName("product_id")] Guid? ProductId,
    [property: JsonPropertyName("quantity")] int? Quantity,
    [property: JsonPropertyName("company_id")] Guid? CompanyId,
    [property: JsonPropertyName("branch_id")] Guid? BranchId,
    [property: JsonPropertyName("po_number")] string? PoNumber,
    [property: JsonPropertyName("message")] LocalizedMessage? Message);
