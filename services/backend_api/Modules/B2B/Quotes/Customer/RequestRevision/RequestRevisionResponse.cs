using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestRevision;

/// <summary>
/// Spec 021 contract §2.6 — 200 success response. Returns the post-transition
/// quote summary so the client can update its in-memory cache.
///
/// <para>
/// Note on <see cref="State"/>: the contract states the post-transition state is
/// <c>drafted</c> ("operator-only-visible"). The customer-facing client typically
/// re-displays the quote as "in revision" — UI maps the wire token to that copy.
/// </para>
/// </summary>
public sealed record RequestRevisionResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("state")] string State);
