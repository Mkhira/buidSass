using BackendApi.Modules.Reviews.Admin;
using BackendApi.Modules.Reviews.Authorization;
using BackendApi.Modules.Reviews.Customer;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.PolicyAdmin.DeleteWordlistTerm;

public static class DeleteWordlistTermEndpoint
{
    public static IEndpointRouteBuilder MapDeleteWordlistTermEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapDelete("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] DeleteWordlistTermRequest? body,
        HttpContext context,
        [FromServices] DeleteWordlistTermHandler handler,
        CancellationToken ct)
    {
        if (!AdminReviewsResponseFactory.HasPermissionClaim(context, ReviewsPermissions.PolicyAdmin))
        {
            return AdminReviewsResponseFactory.Problem(context, 403,
                ReviewReasonCode.PolicyForbidden, "reviews.policy_admin permission required.");
        }

        if (body is null || string.IsNullOrWhiteSpace(body.MarketCode) || string.IsNullOrWhiteSpace(body.Term))
        {
            return AdminReviewsResponseFactory.Problem(context, 400,
                ReviewReasonCode.PolicyWordlistTermInvalid,
                "Both market_code and term are required.");
        }

        var market = ReviewsResponseFactory.TryNormalize(body.MarketCode);
        if (market is null)
        {
            return AdminReviewsResponseFactory.Problem(context, 400,
                ReviewReasonCode.AggregateMarketInvalid, "Unknown market code.");
        }

        await handler.HandleAsync(market, body.Term, ct);
        return Results.NoContent();
    }
}

public sealed record DeleteWordlistTermRequest(string MarketCode, string Term);
