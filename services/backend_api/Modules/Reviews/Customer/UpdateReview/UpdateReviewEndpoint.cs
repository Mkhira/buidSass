using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Customer.UpdateReview;

public static class UpdateReviewEndpoint
{
    public static IEndpointRouteBuilder MapUpdateReviewEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPatch("/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        UpdateReviewRequest? body,
        HttpContext context,
        UpdateReviewHandler handler,
        CancellationToken ct)
    {
        var customerId = ReviewsResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return ReviewsResponseFactory.Problem(context, 401,
                Primitives.ReviewReasonCode.ReportUnauthenticated,
                "Authentication required.");
        }

        if (body is null)
        {
            return ReviewsResponseFactory.Problem(context, 400,
                Primitives.ReviewReasonCode.BodyLengthInvalid,
                "Request body is required.");
        }

        uint? ifMatch = null;
        if (context.Request.Headers.TryGetValue("If-Match", out var ifMatchHeader)
            && uint.TryParse(ifMatchHeader.ToString().Trim('"'), out var parsed))
        {
            ifMatch = parsed;
        }

        var result = await handler.HandleAsync(customerId.Value, id, ifMatch, body, ct);
        if (!result.IsSuccess)
        {
            return ReviewsResponseFactory.Problem(context, result.Status, result.ReasonCode!, "Edit rejected.", result.Detail);
        }

        return Results.Ok(result.Response);
    }
}
