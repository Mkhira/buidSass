using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Customer.ListMyReviews;

public static class ListMyReviewsEndpoint
{
    public static IEndpointRouteBuilder MapListMyReviewsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/me", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ListMyReviewsHandler handler,
        CancellationToken ct,
        string? state = null,
        string? cursor = null,
        int limit = 20)
    {
        var customerId = ReviewsResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return ReviewsResponseFactory.Problem(context, 401,
                Primitives.ReviewReasonCode.ReportUnauthenticated,
                "Authentication required.");
        }

        // Reject unknown `state` at the boundary instead of silently treating
        // it as "no filter" — eliminates ambiguity for clients. Canonicalize
        // the value so the handler sees a single normalized form regardless of
        // input casing (CodeRabbit PR #47 round 2).
        string? canonicalState = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            canonicalState = state.Trim().ToLowerInvariant();
            if (canonicalState is not ("pending_moderation" or "visible" or "flagged" or "hidden" or "deleted"))
            {
                return ReviewsResponseFactory.Problem(context, 400,
                    Primitives.ReviewReasonCode.ModerationInvalidState,
                    "state must be one of: pending_moderation, visible, flagged, hidden, deleted.");
            }
        }

        // Reject malformed cursor / limit at the boundary so the handler doesn't
        // pre-emptively coerce — caller gets a stable 400 with reasonCode.
        DateTimeOffset? cursorBefore = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            // Strict ISO-8601 round-trip ("O") — DateTimeOffset.TryParse is too
            // permissive (accepts e.g. "2026/04/30 10:00") and drifts from the
            // contract-level message.
            if (!DateTimeOffset.TryParseExact(
                    cursor,
                    "O",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var parsed))
            {
                return ReviewsResponseFactory.Problem(context, 400,
                    Primitives.ReviewReasonCode.MediaInvalidSignedUrl,
                    "cursor must be an ISO-8601 timestamp.");
            }
            cursorBefore = parsed;
        }
        if (limit < 1 || limit > 100)
        {
            return ReviewsResponseFactory.Problem(context, 400,
                Primitives.ReviewReasonCode.RatingOutOfRange,
                "limit must be between 1 and 100.");
        }

        var response = await handler.HandleAsync(customerId.Value, canonicalState, cursorBefore, limit, ct);
        return Results.Ok(response);
    }
}
