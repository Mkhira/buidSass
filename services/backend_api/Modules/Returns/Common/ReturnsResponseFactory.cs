using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Returns.Common;

public static class ReturnsResponseFactory
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
            Type = $"https://errors.dental-commerce/returns/{reasonCode}",
            Instance = context.Request.Path,
        };
        problem.Extensions["reasonCode"] = reasonCode;
        if (extensions is not null)
        {
            foreach (var (k, v) in extensions) problem.Extensions[k] = v;
        }
        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }

    public static Guid? ResolveAccountId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>
    /// Resolves the market claim with consistent fallback across all Returns endpoints.
    /// Checks <c>market_code</c> first (admin convention), falls back to <c>market</c>
    /// (customer convention), then to a default of <c>"KSA"</c>. Comparison is case-
    /// insensitive; the returned value is always uppercase canonical (e.g. "KSA", "EG").
    /// </summary>
    public static string ResolveMarketCode(HttpContext context)
    {
        var raw = context.User.FindFirst("market_code")?.Value
            ?? context.User.FindFirst("market")?.Value;
        if (string.IsNullOrWhiteSpace(raw)) return "KSA";
        var trimmed = raw.Trim().ToUpperInvariant();
        return trimmed == "EG" ? "EG" : "KSA";
    }
}
