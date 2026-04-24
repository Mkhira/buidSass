using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Inventory.Admin.Common;

public static class AdminInventoryResponseFactory
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
            Type = $"https://errors.dental-commerce/inventory/{reasonCode}",
            Instance = context.Request.Path,
        };

        problem.Extensions["reasonCode"] = reasonCode;
        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }

    public static Guid ResolveActorAccountId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(sub, out var accountId)
            ? accountId
            : Guid.Empty;
    }
}
