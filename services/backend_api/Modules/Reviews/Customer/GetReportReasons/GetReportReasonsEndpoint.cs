using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Customer.GetReportReasons;

public static class GetReportReasonsEndpoint
{
    public static IEndpointRouteBuilder MapGetReportReasonsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/report-reasons", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static IResult HandleAsync([FromServices] GetReportReasonsHandler handler)
    {
        return Results.Ok(handler.Handle());
    }
}
