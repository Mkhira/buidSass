using BackendApi.Modules.Verification.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Verification.Admin;

/// <summary>
/// Reviewer-side response shaping. Mirrors <see cref="Customer.VerificationResponseFactory"/>
/// but reads admin-specific claims (reviewer id, assigned markets, permission set).
/// </summary>
public static class AdminVerificationResponseFactory
{
    public static IResult Problem(
        HttpContext context,
        int statusCode,
        string reasonCode,
        string title,
        string? detail = null,
        IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail ?? string.Empty,
            Type = $"https://errors.dental-commerce/verification/{reasonCode}",
            Instance = context.Request.Path,
        };
        problem.Extensions["reasonCode"] = reasonCode;
        if (extensions is not null)
        {
            foreach (var (k, v) in extensions)
            {
                problem.Extensions[k] = v;
            }
        }
        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }

    public static IResult Problem(
        HttpContext context,
        int statusCode,
        VerificationReasonCode reasonCode,
        string title,
        string? detail = null,
        IDictionary<string, object?>? extensions = null)
        => Problem(context, statusCode, reasonCode.ToWireValue(), title, detail, extensions);

    /// <summary>Resolves the reviewer's <c>sub</c> JWT claim.</summary>
    public static Guid? ResolveReviewerId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>
    /// Resolves the reviewer's assigned markets from JWT claims. Reads either a
    /// single <c>market_code</c> / <c>market</c> claim or a <c>markets</c>
    /// CSV claim.
    /// </summary>
    /// <remarks>
    /// Fails closed: returns an empty set when the claim is missing or every
    /// listed market fails normalization. Callers MUST handle the empty-set
    /// case (the queue handler returns an empty response; the detail / decide
    /// handlers return NotFound). This avoids broadening reviewer access by
    /// silently defaulting to "ksa" when claims are absent or malformed
    /// (Principle 5 — market-specific behavior MUST come from configuration,
    /// not from in-code defaults that hide a missing claim).
    /// </remarks>
    public static IReadOnlySet<string> ResolveAssignedMarkets(HttpContext context)
    {
        var multi = context.User.FindFirst("markets")?.Value;
        if (!string.IsNullOrWhiteSpace(multi))
        {
            return multi.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeMarket)
                .Where(m => m is not null)
                .Cast<string>()
                .ToHashSet();
        }

        var single = context.User.FindFirst("market_code")?.Value
            ?? context.User.FindFirst("market")?.Value;
        var normalized = NormalizeMarket(single);
        return normalized is null
            ? new HashSet<string>()  // fail closed — caller MUST treat as no scope
            : new HashSet<string> { normalized };
    }

    /// <summary>
    /// Validates a market code against the registered set. The wire-format
    /// values match the database CHECK constraint
    /// (<see cref="Entities.VerificationMarketSchema"/>) and the
    /// <c>verification_market_schemas</c> rows seeded by
    /// <see cref="Seeding.VerificationReferenceDataSeeder"/>. The set is sourced
    /// here from the same canonical list rather than hardcoded inline at the
    /// callsite — Principle 5 / market-config rule. New markets ship as a seeder
    /// version bump + a SupportedMarketCodes update in the same PR.
    /// </summary>
    public static IReadOnlySet<string> SupportedMarketCodes { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "eg", "ksa" };

    private static string? NormalizeMarket(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var trimmed = raw.Trim().ToLowerInvariant();
        return SupportedMarketCodes.Contains(trimmed) ? trimmed : null;
    }
}
