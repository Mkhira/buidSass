using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Search.Customer.Common;

public static class CustomerSearchResponseFactory
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
            Type = $"https://errors.dental-commerce/search/{reasonCode}",
            Instance = context.Request.Path,
        };
        problem.Extensions["reasonCode"] = reasonCode;

        if (statusCode == StatusCodes.Status503ServiceUnavailable)
        {
            context.Response.Headers.RetryAfter = "5";
        }

        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }
}
