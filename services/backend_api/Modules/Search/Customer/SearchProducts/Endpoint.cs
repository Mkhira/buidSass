using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Search.Customer.Common;
using BackendApi.Modules.Search.Primitives;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Search.Customer.SearchProducts;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapSearchProductsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/products", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        SearchProductsRequest request,
        HttpContext context,
        ISearchEngine searchEngine,
        CatalogDbContext catalogDbContext,
        QueryLogger queryLogger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await SearchProductsHandler.HandleAsync(request, searchEngine, catalogDbContext, queryLogger, cancellationToken);
            if (!result.IsSuccess)
            {
                return CustomerSearchResponseFactory.Problem(
                    context,
                    result.StatusCode,
                    result.ReasonCode!,
                    result.Title!,
                    result.Detail!);
            }

            return Results.Ok(result.Response);
        }
        catch (Exception)
        {
            return CustomerSearchResponseFactory.Problem(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "search.engine_unavailable",
                "Search engine unavailable",
                "Search is temporarily unavailable. Please retry shortly.");
        }
    }
}
