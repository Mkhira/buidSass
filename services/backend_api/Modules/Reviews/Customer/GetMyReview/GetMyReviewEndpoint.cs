using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Customer.GetMyReview;

public static class GetMyReviewEndpoint
{
    public static IEndpointRouteBuilder MapGetMyReviewEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/me/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        GetMyReviewHandler handler,
        CancellationToken ct)
    {
        var customerId = ReviewsResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return ReviewsResponseFactory.Problem(context, 401,
                Primitives.ReviewReasonCode.ReportUnauthenticated,
                "Authentication required.");
        }

        var response = await handler.HandleAsync(customerId.Value, id, ct);
        return response is null
            ? Results.NotFound()
            : Results.Ok(response);
    }
}
