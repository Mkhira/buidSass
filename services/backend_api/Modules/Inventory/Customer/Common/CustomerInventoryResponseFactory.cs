using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Inventory.Customer.Common;

public static class CustomerInventoryResponseFactory
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
            Type = $"https://errors.dental-commerce/inventory/{reasonCode}",
            Instance = context.Request.Path,
        };

        problem.Extensions["reasonCode"] = reasonCode;
        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }
}
