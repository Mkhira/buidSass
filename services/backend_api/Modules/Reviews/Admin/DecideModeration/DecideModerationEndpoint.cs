using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Reviews.RateLimit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Admin.DecideModeration;

public static class DecideModerationEndpoint
{
    public static IEndpointRouteBuilder MapDecideModerationEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/decide", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
 [FromBody] DecideModerationRequest? body,
        HttpContext context,
 [FromServices] DecideModerationHandler handler,
 [FromServices] ReviewRateLimiter rateLimiter,
        CancellationToken ct)
    {
        var actorId = AdminReviewsResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return AdminReviewsResponseFactory.Problem(context, 401,
                ReviewReasonCode.ModerationForbidden, "Admin authentication required.");
        }

        if (!rateLimiter.TryAcquire(ReviewRateLimits.ModerationDecision, actorId.Value,
                ReviewRateLimits.ModeratorCapacityPerHour, ReviewRateLimits.Window))
        {
            return AdminReviewsResponseFactory.Problem(context, 429,
                ReviewReasonCode.ModerationRateLimitExceeded,
                "Moderation decision rate limit exceeded.");
        }

        var (ok, reason, detail) = DecideModerationValidator.Validate(body);
        if (!ok)
        {
            return AdminReviewsResponseFactory.Problem(context, 400, reason!, "Decision validation failed.", detail);
        }

        uint? ifMatch = null;
        if (context.Request.Headers.TryGetValue("If-Match", out var ifMatchHeader)
            && uint.TryParse(ifMatchHeader.ToString().Trim('"'), out var parsed))
        {
            ifMatch = parsed;
        }

        var hasModerator = AdminReviewsResponseFactory.HasModeratorPermission(context);
        var hasSuperAdmin = AdminReviewsResponseFactory.HasSuperAdmin(context);

        var result = await handler.HandleAsync(actorId.Value, hasModerator, hasSuperAdmin, id, ifMatch, body!, ct);
        if (!result.IsSuccess)
        {
            return AdminReviewsResponseFactory.Problem(context, result.Status, result.ReasonCode!, "Decision rejected.", result.Detail);
        }

        return Results.Ok(result.Response);
    }
}
