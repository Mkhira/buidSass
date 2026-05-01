using BackendApi.Modules.Reviews.Primitives;
using Microsoft.AspNetCore.Authorization;
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
        DecideModerationRequest? body,
        HttpContext context,
        DecideModerationHandler handler,
        CancellationToken ct)
    {
        var actorId = AdminReviewsResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return AdminReviewsResponseFactory.Problem(context, 401,
                ReviewReasonCode.ModerationForbidden, "Admin authentication required.");
        }

        var (ok, reason, detail) = DecideModerationValidator.Validate(body);
        if (!ok)
        {
            return AdminReviewsResponseFactory.Problem(context, 400, reason!, "Decision validation failed.", detail);
        }

        // If-Match present but unparseable → 400 with version-conflict reason.
        // Treating it as "no guard" silently disables the optimistic-concurrency
        // check, which is a security/data-integrity risk.
        uint? ifMatch = null;
        if (context.Request.Headers.TryGetValue("If-Match", out var ifMatchHeader)
            && !string.IsNullOrWhiteSpace(ifMatchHeader.ToString()))
        {
            if (!uint.TryParse(ifMatchHeader.ToString().Trim('"'), out var parsed))
            {
                return AdminReviewsResponseFactory.Problem(context, 400,
                    ReviewReasonCode.ModerationVersionConflict,
                    "If-Match header must be a valid xmin row-version uint.");
            }
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
