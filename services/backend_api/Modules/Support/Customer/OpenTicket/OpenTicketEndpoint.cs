using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Customer.OpenTicket;

public static class OpenTicketEndpoint
{
    public static IEndpointRouteBuilder MapOpenTicketEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    public sealed record OpenTicketRequest(
        string? Category,
        string? Priority,
        string? Locale,
        string? Subject,
        string? Body,
        string? LinkedEntityKind,
        Guid? LinkedEntityId,
        IReadOnlyList<Guid>? AttachmentIds);

    private static async Task<IResult> HandleAsync(
        [FromBody] OpenTicketRequest? body,
        HttpContext context,
        [FromServices] OpenTicketHandler handler,
        CancellationToken ct)
    {
        var customerId = SupportResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return SupportResponseFactory.Problem(context, 401,
                TicketReasonCode.CustomerForbidden,
                "Authentication required.");
        }

        if (body is null)
        {
            return SupportResponseFactory.Problem(context, 400,
                TicketReasonCode.SubjectRequired,
                "Request body is required.");
        }

        var marketCode = SupportResponseFactory.ResolveMarketCode(context);
        var cmd = new OpenTicketCommand(
            CustomerId: customerId.Value,
            CustomerMarketCode: marketCode,
            Locale: body.Locale ?? "en",
            Category: body.Category ?? string.Empty,
            Priority: body.Priority ?? "normal",
            Subject: body.Subject ?? string.Empty,
            Body: body.Body ?? string.Empty,
            LinkedEntityKind: body.LinkedEntityKind,
            LinkedEntityId: body.LinkedEntityId,
            AttachmentIds: body.AttachmentIds);

        var result = await handler.HandleAsync(cmd, ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotOwned => 403,
                TicketReasonCode.MarketCodeUnresolvable => 400,
                _ => 400,
            };
            return SupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "Ticket creation rejected.", result.Detail);
        }

        return Results.Created($"/api/customer/support-tickets/{result.TicketId}", new
        {
            ticket_id = result.TicketId,
            state = result.State,
            market_code = result.MarketCode,
            first_response_due_utc = result.FirstResponseDueUtc,
            resolution_due_utc = result.ResolutionDueUtc,
            assigned_agent_id = result.AssignedAgentId,
        });
    }
}
