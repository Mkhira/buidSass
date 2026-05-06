using BackendApi.Modules.Support.Authorization;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Agent.ReplyAsAgent;

public static class ReplyAsAgentEndpoint
{
    public sealed record ReplyRequest(string? Body, bool? RequestInfoTransition);

    public static IEndpointRouteBuilder MapReplyAsAgentEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{ticketId:guid}/replies", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        [FromBody] ReplyRequest? body,
        HttpContext context,
        [FromServices] ReplyAsAgentHandler handler,
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

        var actorRole = actorIsLeadOrSuperAdmin
            ? (AdminSupportResponseFactory.HasSuperAdmin(context) ? SupportPermissions.SuperAdmin : SupportPermissions.SupportLead)
            : SupportPermissions.SupportAgent;

        var result = await handler.HandleAsync(new ReplyAsAgentCommand(
            TicketId: ticketId,
            ActorId: actorId.Value,
            ActorIsAssigned: actorIsAssigned,
            ActorIsLeadOrSuperAdmin: actorIsLeadOrSuperAdmin,
            ActorRole: actorRole,
            Body: body?.Body ?? string.Empty,
            RequestInfoTransition: body?.RequestInfoTransition ?? true), ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.ActionRequiresAssignment => 403,
                TicketReasonCode.ClosedTerminal => 409,
                TicketReasonCode.VersionConflict => 409,
                _ => 400,
            };
            return AdminSupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "Reply rejected.", result.Detail);
        }

        return Results.Ok(new { message_id = result.MessageId, new_state = result.NewState });
    }
}
