using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Admin.ListQuoteQueue;

/// <summary>
/// Spec 021 contract §4.1 — admin queue query parameters. All fields optional;
/// defaults: oldest-first, non-terminal states, caller's market.
/// </summary>
public sealed record ListQuoteQueueRequest(
    string? Market,
    string? StatesCsv,
    Guid? CompanyId,
    Guid? CustomerId,
    int? AgeMinBusinessDays,
    string? Search,
    string? Sort,
    int Page,
    int PageSize);

public sealed record ListQuoteQueueRow(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("market_code")] string MarketCode,
    [property: JsonPropertyName("company_id")] Guid? CompanyId,
    [property: JsonPropertyName("customer_id")] Guid CustomerId,
    [property: JsonPropertyName("po_number")] string? PoNumber,
    [property: JsonPropertyName("requested_at")] DateTimeOffset RequestedAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("age_business_days")] int AgeBusinessDays,
    [property: JsonPropertyName("sla_signal")] string SlaSignal,
    [property: JsonPropertyName("totals_summary")] object? TotalsSummary);

public sealed record ListQuoteQueueResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<ListQuoteQueueRow> Items,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("total")] int Total);
