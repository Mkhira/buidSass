using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;

/// <summary>
/// Spec 021 contract §2.1 — request DTO for
/// <c>POST /api/customer/quotes/from-cart</c>. Wire shape (snake_case JSON):
/// <code>
/// {
///   "company_id":  "01HX...",        // optional: omit for individual-customer quote
///   "branch_id":   "01HX...",        // optional: only meaningful with company_id
///   "po_number":   "PO-2026-0042",   // optional: required only when company.po_required=true
///   "message":     { "en": "...",    // optional: at least one locale when present
///                    "ar": "..." }
/// }
/// </code>
/// All fields are nullable — the validator is the load-bearing contract for required-when-X
/// rules (PO required when company.po_required=true; message-at-least-one-locale).
/// </summary>
public sealed record RequestQuoteFromCartRequest(
    [property: JsonPropertyName("company_id")] Guid? CompanyId,
    [property: JsonPropertyName("branch_id")] Guid? BranchId,
    [property: JsonPropertyName("po_number")] string? PoNumber,
    [property: JsonPropertyName("message")] LocalizedMessage? Message);

/// <summary>
/// Bilingual message envelope: at least one of <see cref="En"/> or <see cref="Ar"/> MUST
/// be non-empty when the parent <c>message</c> object is present (validator-enforced per
/// Principle 4 — Arabic/English parity, no machine translation).
/// </summary>
public sealed record LocalizedMessage(
    [property: JsonPropertyName("en")] string? En,
    [property: JsonPropertyName("ar")] string? Ar);
