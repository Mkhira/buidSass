using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Catalog.Customer.Common;

public static class CustomerCatalogResponseFactory
{
    public static IResult Problem(
        HttpContext context,
        int statusCode,
        string reasonCode,
        string title,
        string detail)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://errors.dental-commerce/catalog/{reasonCode}",
            Instance = context.Request.Path,
        };
        problem.Extensions["reasonCode"] = reasonCode;
        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }

    public static string ResolveMarket(HttpContext context, string? queryMarket)
    {
        if (!string.IsNullOrWhiteSpace(queryMarket))
        {
            return queryMarket.Trim().ToLowerInvariant();
        }

        // TODO(spec-004 seam): pull preferred market from account claim once consumer surfaces
        // pipe it in. For now, default to "ksa" per ADR-010 KSA-primary residency.
        return "ksa";
    }

    public static string ResolveLocale(HttpContext context)
    {
        var header = context.Request.Headers.AcceptLanguage.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return "en";
        }

        var first = header.Split(',').FirstOrDefault()?.Trim() ?? "en";
        var tag = first.Split(';').First().Trim();
        if (tag.StartsWith("ar", StringComparison.OrdinalIgnoreCase))
        {
            return "ar";
        }

        return "en";
    }
}
