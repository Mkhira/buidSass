using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Customer.GetMyTicket;

public static class GetMyTicketEndpoint
{
    public static IEndpointRouteBuilder MapGetMyTicketEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{ticketId:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        HttpContext context,
        [FromServices] GetMyTicketHandler handler,
        CancellationToken ct)
    {
        var customerId = SupportResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return SupportResponseFactory.Problem(context, 401,
                TicketReasonCode.CustomerForbidden, "Authentication required.");
        }

        var result = await handler.HandleAsync(new GetMyTicketQuery(ticketId, customerId.Value), ct);

        if (result.IsNotFound)
        {
            return SupportResponseFactory.Problem(context, 404,
                TicketReasonCode.LinkedEntityNotFound, "Ticket not found.");
        }
        if (result.IsForbidden)
        {
            return SupportResponseFactory.Problem(context, 403,
                TicketReasonCode.CustomerForbidden, "Ticket not owned by this customer.");
        }

        return Results.Ok(result.Ticket);
    }
}
