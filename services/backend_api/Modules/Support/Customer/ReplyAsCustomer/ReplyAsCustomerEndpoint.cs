using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Customer.ReplyAsCustomer;

public static class ReplyAsCustomerEndpoint
{
    public sealed record ReplyRequest(string? Body);

    public static IEndpointRouteBuilder MapReplyAsCustomerEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{ticketId:guid}/replies", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        [FromBody] ReplyRequest? body,
        HttpContext context,
        [FromServices] ReplyAsCustomerHandler handler,
        CancellationToken ct)
    {
        var customerId = SupportResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return SupportResponseFactory.Problem(context, 401,
                TicketReasonCode.CustomerForbidden, "Authentication required.");
        }

        var cmd = new ReplyAsCustomerCommand(
            TicketId: ticketId,
            CustomerId: customerId.Value,
            Body: body?.Body ?? string.Empty);

        var result = await handler.HandleAsync(cmd, ct);
        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.CustomerForbidden => 403,
                TicketReasonCode.ClosedTerminal or TicketReasonCode.ReopenWindowClosed
                    or TicketReasonCode.InvalidTransition => 409,
                TicketReasonCode.VersionConflict => 409,
                _ => 400,
            };
            return SupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "Reply rejected.", result.Detail);
        }

        return Results.Ok(new
        {
            message_id = result.MessageId,
            new_state = result.NewState,
        });
    }
}
