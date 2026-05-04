namespace BackendApi.Modules.B2B.Quotes.Customer.GetMyQuote;

/// <summary>
/// Spec 021 contract §2.4 — request shape for <c>GET /api/customer/quotes/{id}</c>.
/// Just the route parameter; no query string. Modeled as a record for symmetry with
/// the other slices and so future fields (e.g. <c>?include_versions=true</c>) have a
/// place to land.
/// </summary>
public sealed record GetMyQuoteRequest(Guid QuoteId);
