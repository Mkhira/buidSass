using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Customer.OpenRedactionRequestTicket;

public static class OpenRedactionRequestTicketEndpoint
{
    public static IEndpointRouteBuilder MapOpenRedactionRequestTicketEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/redaction-request", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    public sealed record OpenRedactionRequestTicketRequest(
        Guid originating_ticket_id,
        Guid target_message_id,
        string? subject,
        string? reason,
        string? locale);

    private static async Task<IResult> HandleAsync(
        [FromBody] OpenRedactionRequestTicketRequest? body,
        HttpContext context,
        [FromServices] OpenRedactionRequestTicketHandler handler,
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

        var result = await handler.HandleAsync(
            new OpenRedactionRequestTicketCommand(
                CustomerId: customerId.Value,
                CustomerMarketCode: marketCode,
                Locale: body.locale ?? "en",
                OriginatingTicketId: body.originating_ticket_id,
                TargetMessageId: body.target_message_id,
                Subject: body.subject ?? "Redaction request",
                Reason: body.reason ?? string.Empty),
            ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.LinkedEntityNotOwned => 403,
                TicketReasonCode.RedactionRequestMessageNotInOriginatingTicket => 422,
                TicketReasonCode.RedactionRequestAlreadyRedacted => 409,
                _ => 400,
            };
            return SupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "Redaction request rejected.", result.Detail);
        }

        return Results.Created($"/api/customer/support-tickets/{result.TicketId}", new
        {
            ticket_id = result.TicketId,
            state = result.State,
        });
    }
}
