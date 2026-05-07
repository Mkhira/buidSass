using BackendApi.Modules.Support.Customer;
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

        // CodeRabbit Loop-1: validate pagination bounds before dispatching.
        // Negative skip / non-positive take / oversized take are rejected at
        // the edge so the handler can assume sane inputs and the database
        // never sees a request to materialise an unbounded read.
        var normalizedSkip = skip ?? 0;
        var normalizedTake = take ?? 50;
        if (normalizedSkip < 0 || normalizedTake <= 0 || normalizedTake > 100)
        {
            return AdminSupportResponseFactory.Problem(context, 400,
                TicketReasonCode.InvalidTransition,
                "skip must be >= 0 and take must be between 1 and 100.");
        }

        var isSuperAdmin = AdminSupportResponseFactory.HasSuperAdmin(context);
        var marketCode = SupportResponseFactory.ResolveMarketCode(context);

        var result = await handler.HandleAsync(new ListTicketsByCustomerQuery(
            CustomerId: customerId,
            Skip: normalizedSkip,
            Take: normalizedTake,
            StateFilter: state,
            MarketCode: marketCode,
            IsSuperAdmin: isSuperAdmin), ct);

        return Results.Ok(new
        {
            customer_id = customerId,
            items = result.Items,
            total = result.Total,
        });
    }
}
