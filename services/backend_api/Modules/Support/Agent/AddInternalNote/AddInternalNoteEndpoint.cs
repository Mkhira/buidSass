using BackendApi.Modules.Support.Authorization;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Agent.AddInternalNote;

public static class AddInternalNoteEndpoint
{
    public sealed record InternalNoteRequest(string? Body);

    public static IEndpointRouteBuilder MapAddInternalNoteEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{ticketId:guid}/internal-notes", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        [FromBody] InternalNoteRequest? body,
        HttpContext context,
        [FromServices] AddInternalNoteHandler handler,
        CancellationToken ct)
    {
        if (!AdminSupportResponseFactory.HasAgentLevelAccess(context))
        {
            return AdminSupportResponseFactory.Problem(context, 403,
                TicketReasonCode.InternalNoteForbidden,
                "Internal-note posting requires support.agent or higher.");
        }
        var actorId = AdminSupportResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return AdminSupportResponseFactory.Problem(context, 401,
                TicketReasonCode.QueueForbidden, "Authentication required.");
        }

        var actorRole = AdminSupportResponseFactory.HasSuperAdmin(context)
            ? SupportPermissions.SuperAdmin
            : (AdminSupportResponseFactory.HasLeadPermission(context)
                ? SupportPermissions.SupportLead
                : SupportPermissions.SupportAgent);

        var result = await handler.HandleAsync(new AddInternalNoteCommand(
            TicketId: ticketId,
            ActorId: actorId.Value,
            ActorRole: actorRole,
            Body: body?.Body ?? string.Empty), ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.ClosedTerminal => 409,
                _ => 400,
            };
            return AdminSupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "Internal note rejected.", result.Detail);
        }

        return Results.Ok(new { message_id = result.MessageId });
    }
}
