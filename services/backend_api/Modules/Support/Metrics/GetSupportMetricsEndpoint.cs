using BackendApi.Modules.Support.Agent;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Metrics;

public static class GetSupportMetricsEndpoint
{
    public static IEndpointRouteBuilder MapGetSupportMetricsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/metrics", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        [FromServices] GetSupportMetricsHandler handler,
        [FromQuery] string? market_code,
        CancellationToken ct)
    {
        // Per FR-041 / T136b — readable by super_admin OR viewer.finance.
        if (!AdminSupportResponseFactory.HasSuperAdmin(context)
            && !AdminSupportResponseFactory.HasFinanceViewer(context))
        {
            return AdminSupportResponseFactory.Problem(context, 403,
                TicketReasonCode.QueueForbidden,
                "super_admin or viewer.finance permission required.");
        }

        var result = await handler.HandleAsync(new GetSupportMetricsQuery(market_code), ct);

        return Results.Ok(new
        {
            rows = result.Rows.Select(r => new
            {
                market_code = r.MarketCode,
                open_ticket_count = r.OpenTicketCount,
                in_progress_ticket_count = r.InProgressTicketCount,
                waiting_customer_ticket_count = r.WaitingCustomerTicketCount,
                resolved_ticket_count = r.ResolvedTicketCount,
                first_response_breach_count = r.FirstResponseBreachCount,
                resolution_breach_count = r.ResolutionBreachCount,
                average_resolution_minutes = r.AverageResolutionMinutes,
            }),
        });
    }
}
