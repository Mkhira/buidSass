using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Agent.ClaimTicket;

public static class ClaimTicketEndpoint
{
    public static IEndpointRouteBuilder MapClaimTicketEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{ticketId:guid}/claim", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        HttpContext context,
        [FromServices] ClaimTicketHandler handler,
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
                TicketReasonCode.QueueForbidden,
                "Authentication required.");
        }

        // Optional If-Match header carries the row_version (xmin) for optimistic-concurrency.
        uint? ifMatch = null;
        if (context.Request.Headers.TryGetValue("If-Match", out var raw)
            && uint.TryParse(raw.ToString().Trim('"'), out var parsed))
        {
            ifMatch = parsed;
        }

        var result = await handler.HandleAsync(
            new ClaimTicketCommand(ticketId, actorId.Value, ifMatch), ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.AssignmentConflict => 409,
                TicketReasonCode.VersionConflict => 409,
                TicketReasonCode.ClosedTerminal => 409,
                TicketReasonCode.InvalidTransition => 409,
                _ => 400,
            };
            return AdminSupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "Claim rejected.", result.Detail);
        }

        return Results.Ok(new
        {
            assignment_id = result.AssignmentId,
            new_state = result.NewState,
        });
    }
}
