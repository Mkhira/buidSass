using BackendApi.Modules.B2B.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.B2B.Quotes;

/// <summary>
/// Per-module response shaping for the B2B HTTP surface. Mirrors the
/// <c>ReviewsResponseFactory</c> pattern: problem-details for errors with a stable
/// <c>reasonCode</c> extension, plus JWT-claim resolution helpers for the customer
/// surface.
///
/// All B2B endpoints that emit problem-details MUST go through <see cref="Problem"/>
/// so the wire shape stays consistent across customer / approver / admin slices.
/// </summary>
public static class B2BResponseFactory
{
    /// <summary>
    /// Emits an RFC 7807 problem-details response with a stable <c>reasonCode</c>
    /// extension carrying the spec 021 token (e.g. <c>quote.cart_empty</c>).
    /// </summary>
    public static IResult Problem(
        HttpContext context,
        int statusCode,
        QuoteReasonCode reasonCode,
        string title,
        IDictionary<string, object?>? extensions = null)
    {
        var token = reasonCode.ToToken();
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = title, // Cycle B: title and detail unified — Cycle C wires localized ICU strings.
            Type = $"https://errors.dental-commerce/quotes/{token}",
            Instance = context.Request.Path,
        };
        problem.Extensions["reasonCode"] = token;
        if (extensions is not null)
        {
            foreach (var (k, v) in extensions)
            {
                problem.Extensions[k] = v;
            }
        }
        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }

    /// <summary>
    /// Resolves the customer id from the JWT subject claim. Returns <c>null</c> when
    /// the claim is missing or unparseable — caller surfaces 401.
    /// </summary>
    public static Guid? ResolveCustomerId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>
    /// Resolves the customer's market-of-record claim. Spec 021 stores market codes
    /// in lowercase form (<c>eg</c> | <c>ksa</c>) — schema CHECK constraint
    /// <c>chk_quotes_market</c> enforces this. Returns <c>"ksa"</c> when the claim
    /// is missing (matches the seeded market-of-record default for new customer
    /// registrations until spec 014 wires per-customer selection). Returns
    /// <c>null</c> when the claim carries an unknown value — endpoints MUST
    /// surface a problem-details response in that case rather than fall open to a
    /// valid market.
    /// </summary>
    public static string? ResolveMarketCode(HttpContext context)
    {
        var raw = context.User.FindFirst("market_code")?.Value
            ?? context.User.FindFirst("market")?.Value;
        return Normalize(raw);
    }

    /// <summary>
    /// Coerces a market-code string into the canonical lowercase form used by
    /// the b2b schema. Empty / whitespace claims default to <c>"ksa"</c> (platform
    /// default for un-migrated identities). Unknown explicit codes return
    /// <c>null</c> so the caller can fail explicitly instead of silently routing
    /// a malformed identity into a valid market (CodeRabbit Round 1).
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "ksa";
        var t = raw.Trim().ToLowerInvariant();
        return t switch
        {
            "ksa" or "sa" => "ksa",
            "eg" or "egy" => "eg",
            _ => null,
        };
    }
}
