using BackendApi.Modules.Reviews.Primitives;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Admin.GetReviewDetail;

public static class GetReviewDetailEndpoint
{
    public static IEndpointRouteBuilder MapGetReviewDetailEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
 [FromServices] GetReviewDetailHandler handler,
        CancellationToken ct)
    {
        // Read access: moderator, support, viewer.finance.
        var allowed = AdminReviewsResponseFactory.HasModeratorPermission(context)
            || AdminReviewsResponseFactory.HasPermissionClaim(context, "support")
            || AdminReviewsResponseFactory.HasPermissionClaim(context, "viewer.finance");
        if (!allowed)
        {
            return AdminReviewsResponseFactory.Problem(context, 403,
                ReviewReasonCode.ModerationForbidden,
                "Moderator, support, or finance-viewer permission required.");
        }

        var response = await handler.HandleAsync(id, ct);
        return response is null
            ? Results.NotFound()
            : Results.Ok(response);
    }
}
