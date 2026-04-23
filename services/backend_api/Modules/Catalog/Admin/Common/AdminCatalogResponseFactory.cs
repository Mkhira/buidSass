using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Catalog.Admin.Common;

public static class AdminCatalogResponseFactory
{
    public static IResult Problem(HttpContext context, int statusCode, string reasonCode, string title, string detail)
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

    public static Guid ResolveActorAccountId(HttpContext context)
    {
        var raw = context.User.FindFirstValue("sub") ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var parsed) ? parsed : Guid.Empty;
    }

    public static string NormalizeSlug(string raw)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? string.Empty
            : raw.Trim().ToLowerInvariant();
    }
}
