using BackendApi.Modules.Reviews.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Admin.ListModerationQueue;

public static class ListModerationQueueEndpoint
{
    public static IEndpointRouteBuilder MapListModerationQueueEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/queue", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ListModerationQueueHandler handler,
        CancellationToken ct,
        string? state = null,
        string? market_code = null,
        string? triggered_by = null,
        int? community_report_count_min = null,
        bool? media_only = null,
        string? cursor = null,
        int? limit = null)
    {
        if (!AdminReviewsResponseFactory.HasModeratorPermission(context))
        {
            return AdminReviewsResponseFactory.Problem(context, 403,
                ReviewReasonCode.ModerationForbidden,
                "reviews.moderator permission required.");
        }

        DateTimeOffset? cursorAfter = null;
        if (!string.IsNullOrWhiteSpace(cursor) && DateTimeOffset.TryParse(cursor, out var parsed))
        {
            cursorAfter = parsed;
        }

        var query = new ListModerationQueueQuery(
            State: state,
            MarketCode: market_code,
            TriggeredBy: triggered_by,
            CommunityReportCountMin: community_report_count_min,
            MediaOnly: media_only,
            CursorAfter: cursorAfter,
            Limit: limit);

        var response = await handler.HandleAsync(query, ct);
        return Results.Ok(response);
    }
}
