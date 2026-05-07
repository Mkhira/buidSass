using BackendApi.Modules.Support.Agent;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Lead.ForceCloseTicket;

public static class ForceCloseTicketEndpoint
{
    public static IEndpointRouteBuilder MapForceCloseTicketEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{ticketId:guid}/force-close", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    public sealed record ForceCloseTicketRequest(string reason_note);

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        [FromBody] ForceCloseTicketRequest body,
        HttpContext context,
        [FromServices] ForceCloseTicketHandler handler,
        CancellationToken ct)
    {
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
                TicketReasonCode.QueueForbidden, "Authentication required.");
        }

        var result = await handler.HandleAsync(
            new ForceCloseTicketCommand(
                TicketId: ticketId,
                LeadActorId: actorId.Value,
                ReasonNote: body?.reason_note ?? string.Empty),
            ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.InvalidTransition => 409,
                TicketReasonCode.VersionConflict => 409,
                TicketReasonCode.ForceCloseReasonRequired => 400,
                _ => 400,
            };
            return AdminSupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "Force-close rejected.", result.Detail);
        }

        return Results.Ok(new
        {
            prior_state = result.PriorState,
            closed_at_utc = result.ClosedAtUtc,
        });
    }
}
