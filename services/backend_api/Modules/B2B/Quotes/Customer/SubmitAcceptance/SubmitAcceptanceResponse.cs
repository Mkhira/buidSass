using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Customer.SubmitAcceptance;

/// <summary>
/// Spec 021 contract §2.7 — 200 success response on the COMMITTED branch (state moved
/// to <c>pending-approver</c> or <c>accepted</c>). Mirrors the post-transition quote
/// summary so the client can update its in-memory cache without a re-fetch.
///
/// The soft-warning branch uses a different envelope shape
/// (<see cref="SubmitAcceptancePoWarningResponse"/>) so the client can disambiguate
/// "submission committed" vs. "you must acknowledge and re-submit" by JSON shape alone.
/// </summary>
public sealed record SubmitAcceptanceResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("decided_at")] DateTimeOffset? DecidedAt,
    [property: JsonPropertyName("terminal_at")] DateTimeOffset? TerminalAt,
    [property: JsonPropertyName("order_id")] Guid? OrderId);

/// <summary>
/// Spec 021 contract §2.7 — soft-warning envelope returned when
/// <c>company.unique_po_required=false</c> and the PO collides with one or more
/// prior quotes for the company AND the request body's
/// <c>po_warning_acknowledged != true</c>. The handler MUST return 200 OK with this
/// shape and DO NOT transition state. The caller re-submits with
/// <c>po_warning_acknowledged=true</c> to commit; the second call's audit metadata
/// records the acknowledgement.
/// </summary>
public sealed record SubmitAcceptancePoWarningResponse(
    [property: JsonPropertyName("po_warning")] PoWarningPayload PoWarning);

public sealed record PoWarningPayload(
    [property: JsonPropertyName("prior_quote_ids")] IReadOnlyList<Guid> PriorQuoteIds,
    [property: JsonPropertyName("message_key")] string MessageKey);
