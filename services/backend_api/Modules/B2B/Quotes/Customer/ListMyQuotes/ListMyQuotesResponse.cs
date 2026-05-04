using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Customer.ListMyQuotes;

/// <summary>
/// Spec 021 contract §2.3 — paginated list envelope for <c>GET /api/customer/quotes</c>.
/// Wire shape (validated by the Cycle A contract test
/// <c>ListMyQuotesContractTests.Authenticated_request_returns_paginated_envelope</c>):
/// <code>
/// {
///   "items":     [ ... ListMyQuotesItem[] ],
///   "page":      1,
///   "page_size": 20,
///   "total":     42
/// }
/// </code>
/// </summary>
public sealed record ListMyQuotesResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<ListMyQuotesItem> Items,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("total")] int Total);

/// <summary>
/// Per-row summary for the customer quote list. Intentionally compact — the detail
/// endpoint (§2.4 / GetMyQuote) returns the full version + line items + totals on demand.
/// </summary>
public sealed record ListMyQuotesItem(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("market_code")] string MarketCode,
    [property: JsonPropertyName("company_id")] Guid? CompanyId,
    [property: JsonPropertyName("branch_id")] Guid? BranchId,
    [property: JsonPropertyName("po_number")] string? PoNumber,
    [property: JsonPropertyName("requested_at")] DateTimeOffset RequestedAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("decided_at")] DateTimeOffset? DecidedAt,
    [property: JsonPropertyName("current_version_number")] int? CurrentVersionNumber);
