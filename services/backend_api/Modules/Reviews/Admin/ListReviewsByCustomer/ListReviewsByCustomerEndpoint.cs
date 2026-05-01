using BackendApi.Modules.Reviews.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Admin.ListReviewsByCustomer;

public static class ListReviewsByCustomerEndpoint
{
    public static IEndpointRouteBuilder MapListReviewsByCustomerEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/by-customer/{customerId:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid customerId,
        HttpContext context,
        ListReviewsByCustomerHandler handler,
        CancellationToken ct,
        string? state = null,
        string? cursor = null,
        int limit = 50)
    {
        var allowed = AdminReviewsResponseFactory.HasModeratorPermission(context)
            || AdminReviewsResponseFactory.HasPermissionClaim(context, "support");
        if (!allowed)
        {
            return AdminReviewsResponseFactory.Problem(context, 403,
                ReviewReasonCode.ModerationForbidden, "Moderator or support permission required.");
        }

        DateTimeOffset? cursorBefore = null;
        if (!string.IsNullOrWhiteSpace(cursor) && DateTimeOffset.TryParse(cursor, out var parsed))
        {
            cursorBefore = parsed;
        }

        var response = await handler.HandleAsync(customerId, state, cursorBefore, limit, ct);
        return Results.Ok(response);
    }
}
