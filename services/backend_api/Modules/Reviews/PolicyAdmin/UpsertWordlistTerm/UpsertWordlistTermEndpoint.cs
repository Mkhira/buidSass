using BackendApi.Modules.Reviews.Admin;
using BackendApi.Modules.Reviews.Authorization;
using BackendApi.Modules.Reviews.Customer;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.PolicyAdmin.UpsertWordlistTerm;

public static class UpsertWordlistTermEndpoint
{
    public static IEndpointRouteBuilder MapUpsertWordlistTermEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPut("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        UpsertWordlistTermRequest? body,
        HttpContext context,
        UpsertWordlistTermHandler handler,
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
                ReviewReasonCode.PolicyWordlistTermInvalid, "Request body is required.");
        }

        var market = ReviewsResponseFactory.TryNormalize(body.MarketCode);
        if (market is null)
        {
            return AdminReviewsResponseFactory.Problem(context, 400,
                ReviewReasonCode.AggregateMarketInvalid, "Unknown market code.");
        }

        var normalizedRequest = body with { MarketCode = market };
        var result = await handler.HandleAsync(actorId.Value, normalizedRequest, ct);
        if (!result.IsSuccess)
        {
            return AdminReviewsResponseFactory.Problem(context, result.Status, result.ReasonCode!, "Upsert rejected.", result.Detail);
        }
        return Results.Ok(result.Response);
    }
}
