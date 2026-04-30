using BackendApi.Modules.Reviews.Admin;
using BackendApi.Modules.Reviews.Authorization;
using BackendApi.Modules.Reviews.Customer;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.PolicyAdmin.ListWordlistTerms;

public static class ListWordlistTermsEndpoint
{
    public static IEndpointRouteBuilder MapListWordlistTermsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
 [FromServices] ListWordlistTermsHandler handler,
        CancellationToken ct,
        string? market_code = null)
    {
        if (!AdminReviewsResponseFactory.HasPermissionClaim(context, ReviewsPermissions.PolicyAdmin))
        {
            return AdminReviewsResponseFactory.Problem(context, 403,
                ReviewReasonCode.PolicyForbidden, "reviews.policy_admin permission required.");
        }

        var market = ReviewsResponseFactory.TryNormalize(market_code);
        if (market is null)
        {
            return AdminReviewsResponseFactory.Problem(context, 400,
                ReviewReasonCode.AggregateMarketInvalid, "market_code query parameter is required.");
        }

        var response = await handler.HandleAsync(market, ct);
        return Results.Ok(response);
    }
}
