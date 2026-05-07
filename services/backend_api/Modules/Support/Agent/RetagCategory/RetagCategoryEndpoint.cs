using BackendApi.Modules.Support.Authorization;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Agent.RetagCategory;

public static class RetagCategoryEndpoint
{
    public sealed record RetagCategoryRequest(string Category, string? Justification);

    public static IEndpointRouteBuilder MapRetagCategoryEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{ticketId:guid}/retag-category", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        [FromBody] RetagCategoryRequest? body,
        HttpContext context,
        [FromServices] RetagCategoryHandler handler,
        CancellationToken ct)
    {
        if (!AdminSupportResponseFactory.HasAgentLevelAccess(context))
        {
            return AdminSupportResponseFactory.Problem(context, 403,
                TicketReasonCode.QueueForbidden,
                "support.agent permission required.");
        }
        var actorId = AdminSupportResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return AdminSupportResponseFactory.Problem(context, 401,
                TicketReasonCode.QueueForbidden, "Authentication required.");
        }
        if (body is null || string.IsNullOrWhiteSpace(body.Category))
        {
            return AdminSupportResponseFactory.Problem(context, 400,
                TicketReasonCode.InvalidTransition, "category is required.");
        }

        var actorRole = AdminSupportResponseFactory.HasSuperAdmin(context)
            ? SupportPermissions.SuperAdmin
            : (AdminSupportResponseFactory.HasLeadPermission(context)
                ? SupportPermissions.SupportLead
                : SupportPermissions.SupportAgent);

        var result = await handler.HandleAsync(new RetagCategoryCommand(
            TicketId: ticketId,
            ActorId: actorId.Value,
            ActorRole: actorRole,
            NewCategory: body.Category,
            Justification: body.Justification), ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.ClosedTerminal => 409,
                TicketReasonCode.VersionConflict => 409,
                _ => 400,
            };
            return AdminSupportResponseFactory.Problem(context, status,
                result.ReasonCode ?? TicketReasonCode.InvalidTransition,
                result.Detail ?? "Retag failed.");
        }

        return Results.Ok(new
        {
            ticket_id = ticketId,
            prior_category = result.PriorCategory,
            new_category = result.NewCategory,
        });
    }
}
