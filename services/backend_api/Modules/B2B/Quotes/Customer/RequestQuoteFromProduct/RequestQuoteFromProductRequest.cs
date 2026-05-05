using System.Text.Json.Serialization;
using BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromProduct;

/// <summary>
/// Spec 021 contract §2.2 — request DTO for
/// <c>POST /api/customer/quotes/from-product</c>. Wire shape (snake_case JSON):
/// <code>
/// {
///   "product_id": "01HX...",         // required
///   "quantity":   5,                  // required, &gt; 0
///   "company_id": "01HX...",         // optional: omit for individual-customer quote
///   "branch_id":  "01HX...",         // optional: only meaningful with company_id
///   "po_number":  "PO-2026-0042",    // optional: required only when company.po_required=true
///   "message":    { "en": "...",     // optional: at least one locale when present
///                    "ar": "..." }
/// }
/// </code>
///
/// Reuses <see cref="LocalizedMessage"/> from the cart-intake slice — the bilingual
/// message envelope shape is identical across both quote-creation paths (Principle 4
/// — Arabic/English parity, no machine translation; the validator enforces at-least-
/// one-locale-when-present).
///
/// Behavioral delta vs. <c>RequestQuoteFromCartRequest</c> (contract §2.2):
/// <list type="bullet">
///   <item><c>product_id</c> + <c>quantity</c> are MANDATORY (no cart snapshot).</item>
///   <item>Cart is NOT cleared after a successful request — the customer may keep
///         shopping for other items.</item>
///   <item>Resulting quote carries <c>originating_product_id</c>; the
///         <c>originating_cart_snapshot_json</c> column stays NULL.</item>
///   <item>One additional rejection branch: <c>quote.product_not_quotable</c>
///         when spec 005's catalog flags the product as non-quotable.</item>
/// </list>
/// </summary>
public sealed record RequestQuoteFromProductRequest(
    [property: JsonPropertyName("product_id")] Guid? ProductId,
    [property: JsonPropertyName("quantity")] int? Quantity,
    [property: JsonPropertyName("company_id")] Guid? CompanyId,
    [property: JsonPropertyName("branch_id")] Guid? BranchId,
    [property: JsonPropertyName("po_number")] string? PoNumber,
    [property: JsonPropertyName("message")] LocalizedMessage? Message);
