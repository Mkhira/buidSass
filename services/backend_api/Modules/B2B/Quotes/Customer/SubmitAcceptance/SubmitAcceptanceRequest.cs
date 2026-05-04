using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Customer.SubmitAcceptance;

/// <summary>
/// Spec 021 contract §2.7 — request DTO for
/// <c>POST /api/customer/quotes/{id}/submit-acceptance</c>.
///
/// Wire shape (snake_case JSON, all fields optional):
/// <code>
/// {
///   "po_number": "PO-2026-0042",          // optional override; required when company.po_required=true
///   "po_warning_acknowledged": false,     // set true to confirm a soft warning re: reused PO
///   "tax_preview_drift_acknowledged": false  // set true to confirm a tax-drift conversion (R11)
/// }
/// </code>
///
/// Idempotency-Key is carried in the request HEADER (<c>Idempotency-Key</c>), not the
/// body — matches every other state-mutating quote endpoint. The endpoint enforces
/// header presence and surfaces 400 <c>quote.required_field_missing</c> on absence.
/// </summary>
public sealed record SubmitAcceptanceRequest(
    [property: JsonPropertyName("po_number")] string? PoNumber,
    [property: JsonPropertyName("po_warning_acknowledged")] bool PoWarningAcknowledged,
    [property: JsonPropertyName("tax_preview_drift_acknowledged")] bool TaxPreviewDriftAcknowledged);
