using BackendApi.Modules.Search.Customer.Common;
using BackendApi.Modules.Search.Primitives;
using BackendApi.Modules.Search.Primitives.Normalization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Search.Customer.Autocomplete;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAutocompleteEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/autocomplete", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        AutocompleteRequest request,
        HttpContext context,
        ISearchEngine searchEngine,
        ArabicNormalizer normalizer,
        QueryLogger queryLogger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await AutocompleteHandler.HandleAsync(request, searchEngine, normalizer, queryLogger, cancellationToken);
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
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
