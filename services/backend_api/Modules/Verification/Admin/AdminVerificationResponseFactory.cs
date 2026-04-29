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
    /// Resolves the reviewer's assigned markets. V1 supports either a single
    /// "market_code" / "market" claim (one market) or a "markets" claim with
    /// CSV. Empty / unrecognized values default to "ksa".
    /// </summary>
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
            ? new HashSet<string> { "ksa" }
            : new HashSet<string> { normalized };
    }

    private static string? NormalizeMarket(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var trimmed = raw.Trim().ToLowerInvariant();
        return trimmed is "eg" or "ksa" ? trimmed : null;
    }
}
