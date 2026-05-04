using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Customer.WithdrawQuote;

/// <summary>
/// Spec 021 contract §2.5 — request DTO for
/// <c>POST /api/customer/quotes/{id}/withdraw</c>.
///
/// Wire shape (snake_case JSON, all fields optional):
/// <code>
/// {
///   "reason": "shopping_complete"   // optional — short stable token surfaced
///                                   // on QuoteWithdrawn for downstream notifs
/// }
/// </code>
///
/// The contract doesn't enumerate a body shape. We accept an optional <see cref="Reason"/>
/// token because <see cref="BackendApi.Modules.Shared.QuoteWithdrawn"/> includes a
/// <c>Reason</c> field that spec 025 fans out to the operator queue. Empty body and
/// missing field both resolve to <c>"customer_initiated"</c> in the handler — no
/// validator failure.
/// </summary>
public sealed record WithdrawQuoteRequest(
    [property: JsonPropertyName("reason")] string? Reason);
