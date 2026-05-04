using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestRevision;

/// <summary>
/// Spec 021 contract §2.6 — request DTO for
/// <c>POST /api/customer/quotes/{id}/request-revision</c>.
///
/// Wire shape:
/// <code>
/// {
///   "comment": { "en": "please reduce price",
///                "ar": "يرجى تخفيض السعر" }   // at least one locale required
/// }
/// </code>
///
/// The comment is preserved on the next <c>QuoteVersion</c> (§2.6 behavior:
/// <c>customer_revision_comment</c>) so the operator authoring the new draft can
/// see the buyer's intent inline.
/// </summary>
public sealed record RequestRevisionRequest(
    [property: JsonPropertyName("comment")] LocalizedComment? Comment);

/// <summary>
/// Bilingual comment envelope. At least one of <see cref="En"/> / <see cref="Ar"/>
/// MUST be non-empty (Principle 4 — Arabic/English parity, no machine translation).
/// </summary>
public sealed record LocalizedComment(
    [property: JsonPropertyName("en")] string? En,
    [property: JsonPropertyName("ar")] string? Ar);
