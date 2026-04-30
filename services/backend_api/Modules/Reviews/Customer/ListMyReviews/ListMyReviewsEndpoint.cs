using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Customer.ListMyReviews;

public static class ListMyReviewsEndpoint
{
    public static IEndpointRouteBuilder MapListMyReviewsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/me", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ListMyReviewsHandler handler,
        CancellationToken ct,
        string? state = null,
        string? cursor = null,
        int limit = 20)
    {
        var customerId = ReviewsResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return ReviewsResponseFactory.Problem(context, 401,
                Primitives.ReviewReasonCode.ReportUnauthenticated,
                "Authentication required.");
        }

        // Reject malformed cursor / limit at the boundary so the handler doesn't
        // pre-emptively coerce — caller gets a stable 400 with reasonCode.
        DateTimeOffset? cursorBefore = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!DateTimeOffset.TryParse(cursor, out var parsed))
            {
                return ReviewsResponseFactory.Problem(context, 400,
                    Primitives.ReviewReasonCode.MediaInvalidSignedUrl,
                    "cursor must be an ISO-8601 timestamp.");
            }
            cursorBefore = parsed;
        }
        if (limit < 1 || limit > 100)
        {
            return ReviewsResponseFactory.Problem(context, 400,
                Primitives.ReviewReasonCode.RatingOutOfRange,
                "limit must be between 1 and 100.");
        }

        var response = await handler.HandleAsync(customerId.Value, state, cursorBefore, limit, ct);
        return Results.Ok(response);
    }
}
