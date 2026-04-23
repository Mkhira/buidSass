using BackendApi.Modules.Search.Admin.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Search.Persistence;
using BackendApi.Modules.Search.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Search.Admin.Health;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/health", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("search.read");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ISearchEngine searchEngine,
        SearchDbContext searchDbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await HealthHandler.HandleAsync(searchEngine, searchDbContext, cancellationToken);
            return Results.Ok(response);
        }
        catch
        {
            return AdminSearchResponseFactory.Problem(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "search.engine_unavailable",
                "Search engine unavailable",
                "Search health is temporarily unavailable. Please retry shortly.");
        }
    }
}
