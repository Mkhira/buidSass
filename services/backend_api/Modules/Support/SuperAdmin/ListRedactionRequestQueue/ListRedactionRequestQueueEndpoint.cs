using BackendApi.Modules.Support.Agent;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.SuperAdmin.ListRedactionRequestQueue;

public static class ListRedactionRequestQueueEndpoint
{
    public static IEndpointRouteBuilder MapListRedactionRequestQueueEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/redaction-request-queue", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        [FromServices] ListRedactionRequestQueueHandler handler,
        [FromQuery] string? market_code,
        [FromQuery] int page,
        [FromQuery] int page_size,
        CancellationToken ct)
    {
        if (!AdminSupportResponseFactory.HasSuperAdmin(context))
        {
            return AdminSupportResponseFactory.Problem(context, 403,
                TicketReasonCode.RedactionSuperAdminOnly,
                "super_admin permission required.");
        }

        var result = await handler.HandleAsync(
            new ListRedactionRequestQueueQuery(market_code,
                page == 0 ? 1 : page,
                page_size == 0 ? 50 : page_size),
            ct);

        return Results.Ok(new
        {
            page = result.Page,
            page_size = result.PageSize,
            total_count = result.TotalCount,
            rows = result.Rows.Select(r => new
            {
                ticket_id = r.TicketId,
                customer_id = r.CustomerId,
                market_code = r.MarketCode,
                state = r.State,
                subject = r.Subject,
                created_at_utc = r.CreatedAtUtc,
                first_response_due_utc = r.FirstResponseDueUtc,
                breach_acknowledged_at_first_response = r.BreachAcknowledgedAtFirstResponse,
            }),
        });
    }
}
