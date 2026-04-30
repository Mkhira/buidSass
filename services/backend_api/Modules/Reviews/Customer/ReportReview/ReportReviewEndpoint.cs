using BackendApi.Modules.Reviews.RateLimit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Customer.ReportReview;

public static class ReportReviewEndpoint
{
    public static IEndpointRouteBuilder MapReportReviewEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/report", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
 [FromBody] ReportReviewRequest? body,
        HttpContext context,
 [FromServices] ReportReviewHandler handler,
 [FromServices] ReviewRateLimiter rateLimiter,
        CancellationToken ct)
    {
        var customerId = ReviewsResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return ReviewsResponseFactory.Problem(context, 401,
                Primitives.ReviewReasonCode.ReportUnauthenticated,
                "You must be signed in to report a review.");
        }

        if (!rateLimiter.TryAcquire(ReviewRateLimits.Report, customerId.Value,
                ReviewRateLimits.CustomerCapacityPerHour, ReviewRateLimits.Window))
        {
            return ReviewsResponseFactory.Problem(context, 429,
                Primitives.ReviewReasonCode.RateLimitReportExceeded,
                "Report rate limit exceeded.");
        }

        var (ok, reason, detail) = ReportReviewValidator.Validate(body);
        if (!ok)
        {
            return ReviewsResponseFactory.Problem(context, 400, reason!, "Report validation failed.", detail);
        }

        var result = await handler.HandleAsync(customerId.Value, id, body!, ct);
        if (!result.IsSuccess)
        {
            return ReviewsResponseFactory.Problem(context, result.Status, result.ReasonCode!, "Report rejected.", result.Detail);
        }

        return Results.Created($"/api/customer/reviews/{id}/report/{result.Response!.FlagId}", result.Response);
    }
}
