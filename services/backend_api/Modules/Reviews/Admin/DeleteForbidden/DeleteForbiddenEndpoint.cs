using BackendApi.Modules.Reviews.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Admin.DeleteForbidden;

/// <summary>
/// FR-005a — hard-delete is forbidden for any state. The DELETE method is
/// registered explicitly to surface a stable 405 with the canonical
/// <c>review.row.delete_forbidden</c> reason code rather than leaking the
/// default "Method Not Allowed" body.
/// </summary>
public static class DeleteForbiddenEndpoint
{
    public static IEndpointRouteBuilder MapDeleteForbiddenEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapDelete("/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static IResult HandleAsync(Guid id, HttpContext context)
    {
        return AdminReviewsResponseFactory.Problem(context, 405,
            ReviewReasonCode.RowDeleteForbidden,
            "Reviews cannot be hard-deleted; use POST /decide with to_state=deleted (super_admin only) for soft-state deletion.");
    }
}
