using BackendApi.Modules.Reviews.RateLimit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Customer.SubmitReview;

public static class SubmitReviewEndpoint
{
    public static IEndpointRouteBuilder MapSubmitReviewEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] SubmitReviewRequest? body,
        HttpContext context,
        [FromServices] SubmitReviewHandler handler,
        [FromServices] ReviewRateLimiter rateLimiter,
        CancellationToken ct)
    {
        var customerId = ReviewsResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return ReviewsResponseFactory.Problem(context, 401,
                Primitives.ReviewReasonCode.ReportUnauthenticated,
                "Authentication required.");
        }

        if (!rateLimiter.TryAcquire(ReviewRateLimits.Submission, customerId.Value,
                ReviewRateLimits.CustomerCapacityPerHour, ReviewRateLimits.Window))
        {
            return ReviewsResponseFactory.Problem(context, 429,
                Primitives.ReviewReasonCode.RateLimitSubmissionExceeded,
                "Submission rate limit exceeded.");
        }

        var (ok, reason, detail) = SubmitReviewValidator.Validate(body);
        if (!ok)
        {
            return ReviewsResponseFactory.Problem(context, 400, reason!, "Submission validation failed.", detail);
        }

        var marketCode = ReviewsResponseFactory.ResolveMarketCode(context);
        var result = await handler.HandleAsync(customerId.Value, marketCode, body!, ct);

        if (!result.IsSuccess)
        {
            var status = result.ReasonCode switch
            {
                Primitives.ReviewReasonCode.EligibilityAlreadyReviewed => 400,
                _ => 400,
            };
            return ReviewsResponseFactory.Problem(context, status, result.ReasonCode!, "Submission rejected.", result.Detail, result.Extensions);
        }

        return Results.Created($"/api/customer/reviews/{result.Response!.Id}", result.Response);
    }
}
