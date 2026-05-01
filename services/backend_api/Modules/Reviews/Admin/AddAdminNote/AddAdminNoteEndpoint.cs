using BackendApi.Modules.Reviews.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Reviews.Admin.AddAdminNote;

public static class AddAdminNoteEndpoint
{
    public static IEndpointRouteBuilder MapAddAdminNoteEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/notes", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        [FromBody] AddAdminNoteRequest? body,
        HttpContext context,
        [FromServices] AddAdminNoteHandler handler,
        CancellationToken ct)
    {
        if (!AdminReviewsResponseFactory.HasModeratorPermission(context))
        {
            return AdminReviewsResponseFactory.Problem(context, 403,
                ReviewReasonCode.ModerationForbidden, "reviews.moderator permission required.");
        }
        var actorId = AdminReviewsResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return AdminReviewsResponseFactory.Problem(context, 401,
                ReviewReasonCode.ModerationForbidden, "Admin authentication required.");
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Note))
        {
            return AdminReviewsResponseFactory.Problem(context, 400,
                ReviewReasonCode.ModerationReasonRequired, "Note body is required.");
        }

        var result = await handler.HandleAsync(actorId.Value, id, body.Note, ct);
        if (!result.IsSuccess)
        {
            return AdminReviewsResponseFactory.Problem(context, result.Status, result.ReasonCode!, "Note rejected.", result.Detail);
        }
        return Results.Created($"/api/admin/reviews/{id}/notes/{result.Response!.NoteId}", result.Response);
    }
}
