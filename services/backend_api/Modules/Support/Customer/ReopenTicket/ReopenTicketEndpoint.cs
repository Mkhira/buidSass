using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Customer.ReopenTicket;

public static class ReopenTicketEndpoint
{
    public sealed record ReopenRequest(string? Body);

    public static IEndpointRouteBuilder MapReopenTicketEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{ticketId:guid}/reopen", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        [FromBody] ReopenRequest? body,
        HttpContext context,
        [FromServices] ReopenTicketHandler handler,
        CancellationToken ct)
    {
        var customerId = SupportResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return SupportResponseFactory.Problem(context, 401,
                TicketReasonCode.CustomerForbidden, "Authentication required.");
        }

        var result = await handler.HandleAsync(new ReopenTicketCommand(
            ticketId, customerId.Value, body?.Body), ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.CustomerForbidden => 403,
                TicketReasonCode.ReopenWindowClosed
                    or TicketReasonCode.ReopenCountExceeded
                    or TicketReasonCode.ReopenDisabledForMarket => 409,
                TicketReasonCode.InvalidTransition => 409,
                TicketReasonCode.ClosedTerminal => 409,
                TicketReasonCode.VersionConflict => 409,
                _ => 400,
            };
            return SupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "Reopen rejected.", result.Detail);
        }

        return Results.Ok(new
        {
            new_state = result.NewState,
            reopen_count = result.ReopenCount,
            first_response_due_utc = result.FirstResponseDueUtc,
            resolution_due_utc = result.ResolutionDueUtc,
        });
    }
}
