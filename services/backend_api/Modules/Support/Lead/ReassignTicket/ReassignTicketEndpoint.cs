using BackendApi.Modules.Support.Agent;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Lead.ReassignTicket;

public static class ReassignTicketEndpoint
{
    public static IEndpointRouteBuilder MapReassignTicketEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{ticketId:guid}/reassign", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    public sealed record ReassignTicketRequest(
        Guid target_agent_id,
        string justification_note);

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        [FromBody] ReassignTicketRequest body,
        HttpContext context,
        [FromServices] ReassignTicketHandler handler,
        CancellationToken ct)
    {
        // Lead OR super_admin per FR-014a / FR-039.
        if (!AdminSupportResponseFactory.HasLeadPermission(context)
            && !AdminSupportResponseFactory.HasSuperAdmin(context))
        {
            return AdminSupportResponseFactory.Problem(context, 403,
                TicketReasonCode.QueueForbidden,
                "support.lead permission required.");
        }

        var actorId = AdminSupportResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return AdminSupportResponseFactory.Problem(context, 401,
                TicketReasonCode.QueueForbidden,
                "Authentication required.");
        }

        if (body is null || body.target_agent_id == Guid.Empty)
        {
            return AdminSupportResponseFactory.Problem(context, 400,
                TicketReasonCode.TargetAgentNotInMarket,
                "target_agent_id is required.");
        }

        var result = await handler.HandleAsync(
            new ReassignTicketCommand(
                TicketId: ticketId,
                LeadActorId: actorId.Value,
                TargetAgentId: body.target_agent_id,
                JustificationNote: body.justification_note ?? string.Empty),
            ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.ClosedTerminal => 409,
                TicketReasonCode.AssignmentConflict => 409,
                TicketReasonCode.VersionConflict => 409,
                TicketReasonCode.ReassignJustificationRequired => 400,
                _ => 400,
            };
            return AdminSupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "Reassign rejected.", result.Detail);
        }

        return Results.Ok(new
        {
            assignment_id = result.AssignmentId,
            prior_agent_id = result.PriorAgentId,
        });
    }
}
