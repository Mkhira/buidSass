using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Checkout.Admin.Common;

public static class AdminCheckoutResponseFactory
{
    public static IResult Problem(
        HttpContext context,
        int statusCode,
        string reasonCode,
        string title,
        string detail,
        IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://errors.dental-commerce/checkout/{reasonCode}",
            Instance = context.Request.Path,
        };
        if (extensions is not null)
        {
            foreach (var (k, v) in extensions)
            {
                if (string.Equals(k, "reasonCode", StringComparison.OrdinalIgnoreCase)) continue;
                problem.Extensions[k] = v;
            }
        }
        problem.Extensions["reasonCode"] = reasonCode;
        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }

    public static Guid ResolveActorAccountId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(sub, out var id) && id != Guid.Empty) return id;
        throw new UnauthorizedAccessException("Admin endpoint invoked without a resolvable actor account id.");
    }
}
