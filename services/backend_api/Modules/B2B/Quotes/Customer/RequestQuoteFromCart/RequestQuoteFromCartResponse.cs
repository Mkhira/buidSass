using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;

/// <summary>
/// Spec 021 contract §2.1 — 201 success response. Returned as the JSON body when the
/// quote is successfully created in <c>requested</c> state. Fields chosen to give
/// the client (Flutter customer app, spec 014) everything it needs to navigate to
/// the quote detail screen without a round trip:
/// <list type="bullet">
///   <item><see cref="Id"/> — server-assigned quote id (ULID-shaped Guid).</item>
///   <item><see cref="State"/> — always <c>"requested"</c> at this point.</item>
///   <item><see cref="MarketCode"/> — lowercase token (<c>eg</c> | <c>ksa</c>).</item>
///   <item><see cref="CompanyId"/> / <see cref="BranchId"/> — echoed back; null for individual quotes.</item>
///   <item><see cref="RequestedAt"/> — server-clock timestamp; UI uses this for the SLA stopwatch.</item>
/// </list>
/// </summary>
public sealed record RequestQuoteFromCartResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("market_code")] string MarketCode,
    [property: JsonPropertyName("company_id")] Guid? CompanyId,
    [property: JsonPropertyName("branch_id")] Guid? BranchId,
    [property: JsonPropertyName("requested_at")] DateTimeOffset RequestedAt);
