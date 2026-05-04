using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Customer.WithdrawQuote;

/// <summary>
/// Spec 021 contract §2.5 — 200 success response. Returns the post-transition
/// quote summary so the client can update its in-memory cache without a re-fetch:
/// <list type="bullet">
///   <item><see cref="Id"/> — echoed quote id.</item>
///   <item><see cref="State"/> — always <c>"withdrawn"</c> at this point.</item>
///   <item><see cref="TerminalAt"/> — server-clock timestamp the row hit the
///         terminal state; spec 025 uses this to label the notification.</item>
/// </list>
/// </summary>
public sealed record WithdrawQuoteResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("terminal_at")] DateTimeOffset TerminalAt);
