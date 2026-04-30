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
