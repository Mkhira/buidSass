using Microsoft.AspNetCore.Mvc;
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
 [FromServices] ListMyReviewsHandler handler,
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

        DateTimeOffset? cursorBefore = null;
        if (!string.IsNullOrWhiteSpace(cursor) && DateTimeOffset.TryParse(cursor, out var parsed))
        {
            cursorBefore = parsed;
        }

        var response = await handler.HandleAsync(customerId.Value, state, cursorBefore, limit, ct);
        return Results.Ok(response);
    }
}
