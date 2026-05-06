using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Agent.TransitionToResolved;

public static class TransitionToResolvedEndpoint
{
    public static IEndpointRouteBuilder MapTransitionToResolvedEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{ticketId:guid}/resolve", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        HttpContext context,
        [FromServices] TransitionToResolvedHandler handler,
        [FromServices] SupportDbContext db,
        CancellationToken ct)
    {
        if (!AdminSupportResponseFactory.HasAgentLevelAccess(context))
        {
            return AdminSupportResponseFactory.Problem(context, 403,
                TicketReasonCode.QueueForbidden, "support.agent permission required.");
        }
        var actorId = AdminSupportResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return AdminSupportResponseFactory.Problem(context, 401,
                TicketReasonCode.QueueForbidden, "Authentication required.");
        }

        var assigned = await db.Tickets.AsNoTracking()
            .Where(t => t.Id == ticketId)
            .Select(t => t.AssignedAgentId)
            .FirstOrDefaultAsync(ct);

        var actorIsAssigned = assigned == actorId.Value;
        var actorIsLeadOrSuperAdmin = AdminSupportResponseFactory.HasLeadPermission(context)
            || AdminSupportResponseFactory.HasSuperAdmin(context);

        var result = await handler.HandleAsync(new TransitionToResolvedCommand(
            ticketId, actorId.Value, actorIsAssigned, actorIsLeadOrSuperAdmin), ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.ActionRequiresAssignment => 403,
                TicketReasonCode.ResolvedRequiresAgentReply => 409,
                TicketReasonCode.ClosedTerminal or TicketReasonCode.InvalidTransition => 409,
                TicketReasonCode.VersionConflict => 409,
                _ => 400,
            };
            return AdminSupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "Resolve rejected.", result.Detail);
        }

        return Results.Ok(new { state = "resolved" });
    }
}
