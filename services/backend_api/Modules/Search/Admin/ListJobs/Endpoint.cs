using BackendApi.Modules.Search.Admin.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Search.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Search.Admin.ListJobs;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapListJobsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/jobs", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("search.read");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] ListJobsRequest request,
        HttpContext context,
        SearchDbContext searchDbContext,
        CancellationToken cancellationToken)
    {
        var result = await Handler.HandleAsync(request, searchDbContext, cancellationToken);
        if (!result.IsSuccess)
        {
            return AdminSearchResponseFactory.Problem(
                context,
                result.StatusCode,
                result.ReasonCode!,
                result.Title!,
                result.Detail!);
        }

        return Results.Ok(result.Response);
    }
}
