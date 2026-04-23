using BackendApi.Modules.Search.Customer.Common;
using BackendApi.Modules.Search.Primitives;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Search.Customer.LookupBySkuOrBarcode;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapLookupEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/lookup", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        LookupRequest request,
        HttpContext context,
        ISearchEngine searchEngine,
        QueryLogger queryLogger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await LookupHandler.HandleAsync(request, searchEngine, queryLogger, cancellationToken);
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
        catch
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
