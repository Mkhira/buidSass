using BackendApi.Modules.Reviews.Customer;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Aggregate.ReadProductRating;

/// <summary>
/// Public unauthenticated endpoints — single + batch read of the rating aggregate.
/// Per FR-029 / contract §5: <c>Cache-Control: public, max-age=60</c>.
/// </summary>
public static class ReadProductRatingEndpoint
{
    private const int BatchMax = 100;

    public static IEndpointRouteBuilder MapReadProductRatingEndpoints(this IEndpointRouteBuilder builder)
    {
        // Order matters — register the more-specific batch path BEFORE the {productId:guid}
        // route so a literal "batch" segment isn't parsed as a Guid (it isn't, but keeping
        // the explicit ordering removes any ambiguity for routing diagnostics).
        builder.MapGet("/", HandleBatchAsync).AllowAnonymous();
        builder.MapGet("/{productId:guid}", HandleSingleAsync).AllowAnonymous();
        return builder;
    }

    private static async Task<IResult> HandleSingleAsync(
        Guid productId,
        HttpContext context,
 [FromServices] ReadProductRatingHandler handler,
 TimeProvider time,
        CancellationToken ct,
        string? market_code = null)
    {
        var market = ReviewsResponseFactory.TryNormalize(market_code);
        if (market is null)
        {
            return ReviewsResponseFactory.Problem(context, 400,
                ReviewReasonCode.AggregateMarketInvalid, "Unknown market code.");
        }

        var response = await handler.GetAsync(productId, market, time.GetUtcNow(), ct);
        ApplyCacheHeaders(context);
        return Results.Ok(response);
    }

    private static async Task<IResult> HandleBatchAsync(
        HttpContext context,
 [FromServices] ReadProductRatingHandler handler,
 TimeProvider time,
        CancellationToken ct,
        string? product_ids = null,
        string? market_code = null)
    {
        var market = ReviewsResponseFactory.TryNormalize(market_code);
        if (market is null)
        {
            return ReviewsResponseFactory.Problem(context, 400,
                ReviewReasonCode.AggregateMarketInvalid, "Unknown market code.");
        }

        if (string.IsNullOrWhiteSpace(product_ids))
        {
            return Results.Ok(new ReadProductRatingsResponse(Array.Empty<ReadProductRatingResponse>()));
        }

        var ids = ParseProductIds(product_ids);
        if (ids is null)
        {
            return ReviewsResponseFactory.Problem(context, 400,
                ReviewReasonCode.AggregateMarketInvalid,
                "product_ids must be a comma-separated list of valid GUIDs.");
        }
        if (ids.Count > BatchMax)
        {
            return ReviewsResponseFactory.Problem(context, 400,
                ReviewReasonCode.AggregateMarketInvalid,
                $"Batch size capped at {BatchMax} product ids per call.");
        }

        var response = await handler.GetManyAsync(ids, market, time.GetUtcNow(), ct);
        ApplyCacheHeaders(context);
        return Results.Ok(response);
    }

    private static IReadOnlyList<Guid>? ParseProductIds(string raw)
    {
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids = new List<Guid>(parts.Length);
        foreach (var part in parts)
        {
            if (!Guid.TryParse(part, out var id)) return null;
            ids.Add(id);
        }
        return ids;
    }

    private static void ApplyCacheHeaders(HttpContext context)
    {
        // FR-029 — storefront edge can cache safely for 60 s without divergence
        // from the immediate-on-transition refresh path.
        context.Response.Headers.CacheControl = "public, max-age=60";
    }
}
