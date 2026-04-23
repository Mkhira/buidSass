using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Search.Admin.Common;

public static class AdminSearchResponseFactory
{
    public static IResult Problem(
        HttpContext context,
        int statusCode,
        string reasonCode,
        string title,
        string detail,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://errors.dental-commerce/search/{reasonCode}",
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
}
