using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Customer.ListMyTickets;

public static class ListMyTicketsEndpoint
{
    public static IEndpointRouteBuilder MapListMyTicketsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        [FromQuery] string? state,
        [FromServices] ListMyTicketsHandler handler,
        CancellationToken ct)
    {
        var customerId = SupportResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return SupportResponseFactory.Problem(context, 401,
                TicketReasonCode.CustomerForbidden, "Authentication required.");
        }

        var result = await handler.HandleAsync(new ListMyTicketsQuery(
            CustomerId: customerId.Value,
            Skip: skip ?? 0,
            Take: take ?? 50,
            StateFilter: state), ct);

        return Results.Ok(new
        {
            items = result.Items,
            total = result.Total,
        });
    }
}
