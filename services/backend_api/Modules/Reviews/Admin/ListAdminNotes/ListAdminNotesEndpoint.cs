using BackendApi.Modules.Reviews.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Admin.ListAdminNotes;

public static class ListAdminNotesEndpoint
{
    public static IEndpointRouteBuilder MapListAdminNotesEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}/notes", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        ListAdminNotesHandler handler,
        CancellationToken ct)
    {
        var allowed = AdminReviewsResponseFactory.HasModeratorPermission(context)
            || AdminReviewsResponseFactory.HasPermissionClaim(context, "support");
        if (!allowed)
        {
            return AdminReviewsResponseFactory.Problem(context, 403,
                ReviewReasonCode.ModerationForbidden, "Moderator or support permission required.");
        }

        var response = await handler.HandleAsync(id, ct);
        return Results.Ok(response);
    }
}
