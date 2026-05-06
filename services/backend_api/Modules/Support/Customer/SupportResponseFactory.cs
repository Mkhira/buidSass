namespace BackendApi.Modules.Support.Customer;

using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Per-module response shaping for the Support HTTP surface. Mirrors the
/// per-module helpers used by Verification / Reviews (Problem-Details for errors,
/// JWT-claim resolution for the authenticated customer).
/// </summary>
public static class SupportResponseFactory
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
            Type = $"https://errors.dental-commerce/support/{reasonCode}",
            Instance = context.Request.Path,
        };
        problem.Extensions["reasonCode"] = reasonCode;
        if (extensions is not null)
        {
            foreach (var (k, v) in extensions) problem.Extensions[k] = v;
        }
        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }

    public static Guid? ResolveCustomerId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public static string ResolveMarketCode(HttpContext context)
    {
        var raw = context.User.FindFirst("market_code")?.Value
            ?? context.User.FindFirst("market")?.Value;
        return Normalize(raw);
    }

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "SA";
        var t = raw.Trim().ToUpperInvariant();
        return t switch
        {
            "SA" or "KSA" => "SA",
            "EG" or "EGY" => "EG",
            _ => "SA",
        };
    }
}
