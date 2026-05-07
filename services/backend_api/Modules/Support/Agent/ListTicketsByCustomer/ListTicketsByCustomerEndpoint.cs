using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Agent.ListTicketsByCustomer;

public static class ListTicketsByCustomerEndpoint
{
    public static IEndpointRouteBuilder MapListTicketsByCustomerEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/by-customer/{customerId:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid customerId,
        HttpContext context,
        [FromServices] ListTicketsByCustomerHandler handler,
        CancellationToken ct,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        [FromQuery] string? state = null)
    {
        if (!AdminSupportResponseFactory.HasAgentLevelAccess(context))
        {
            return AdminSupportResponseFactory.Problem(context, 403,
                TicketReasonCode.QueueForbidden,
                "support.agent permission required.");
        }

        var result = await handler.HandleAsync(new ListTicketsByCustomerQuery(
            CustomerId: customerId,
            Skip: skip ?? 0,
            Take: take ?? 50,
            StateFilter: state), ct);

        return Results.Ok(new
        {
            customer_id = customerId,
            items = result.Items,
            total = result.Total,
        });
    }
}
