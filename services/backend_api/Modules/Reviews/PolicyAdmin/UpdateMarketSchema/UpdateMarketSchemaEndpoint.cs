using BackendApi.Modules.Reviews.Admin;
using BackendApi.Modules.Reviews.Authorization;
using BackendApi.Modules.Reviews.Customer;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.PolicyAdmin.UpdateMarketSchema;

public static class UpdateMarketSchemaEndpoint
{
    public static IEndpointRouteBuilder MapUpdateMarketSchemaEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPatch("/{market_code}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        string market_code,
 [FromBody] UpdateMarketSchemaRequest? body,
        HttpContext context,
 [FromServices] UpdateMarketSchemaHandler handler,
        CancellationToken ct)
    {
        if (!AdminReviewsResponseFactory.HasPermissionClaim(context, ReviewsPermissions.PolicyAdmin))
        {
            return AdminReviewsResponseFactory.Problem(context, 403,
                ReviewReasonCode.PolicyForbidden, "reviews.policy_admin permission required.");
        }
        var actorId = AdminReviewsResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return AdminReviewsResponseFactory.Problem(context, 401,
                ReviewReasonCode.PolicyForbidden, "Admin authentication required.");
        }
        if (body is null)
        {
            return AdminReviewsResponseFactory.Problem(context, 400,
                ReviewReasonCode.PolicyMarketValueOutOfRange, "Request body is required.");
        }

        var market = ReviewsResponseFactory.TryNormalize(market_code);
        if (market is null)
        {
            return AdminReviewsResponseFactory.Problem(context, 400,
                ReviewReasonCode.AggregateMarketInvalid, "Unknown market code.");
        }

        var result = await handler.HandleAsync(actorId.Value, market, body, ct);
        if (!result.IsSuccess)
        {
            return AdminReviewsResponseFactory.Problem(context, result.Status, result.ReasonCode!,
                "Market-schema update rejected.", result.Detail);
        }
        return Results.Ok(result.Response);
    }
}
