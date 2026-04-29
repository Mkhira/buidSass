using BackendApi.Modules.Verification.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Verification.Customer;

/// <summary>
/// Verification-module response shaping: Problem Details for errors, JWT
/// claim resolution for the authenticated customer. Mirrors the per-module
/// helpers used by Returns / Checkout / Pricing for stable error envelopes.
/// </summary>
public static class VerificationResponseFactory
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

    /// <summary>
    /// Resolves the authenticated customer id from the JWT <c>sub</c> claim
    /// (with <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/>
    /// fallback for compatibility with the platform Identity scheme).
    /// </summary>
    public static Guid? ResolveCustomerId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>
    /// Resolves the customer's market-of-record claim. Verification stores the
    /// lowercase wire form ("eg", "ksa") to match the database CHECK constraint.
    /// Defaults to "ksa" when the claim is missing — matches platform default.
    /// </summary>
    public static string ResolveMarketCode(HttpContext context)
    {
        var raw = context.User.FindFirst("market_code")?.Value
            ?? context.User.FindFirst("market")?.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "ksa";
        }
        var trimmed = raw.Trim().ToLowerInvariant();
        return trimmed == "eg" ? "eg" : "ksa";
    }
}
