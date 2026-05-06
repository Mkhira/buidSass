using BackendApi.Modules.Support.Agent;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.SuperAdmin.RedactMessage;

public static class RedactMessageEndpoint
{
    public static IEndpointRouteBuilder MapRedactMessageEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{ticketId:guid}/messages/{messageId:guid}/redact", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    public sealed record RedactMessageRequest(
        string reason_note,
        Guid? originating_redaction_request_ticket_id,
        Guid? requesting_customer_id);

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        Guid messageId,
        [FromBody] RedactMessageRequest body,
        HttpContext context,
        [FromServices] RedactMessageHandler handler,
        CancellationToken ct)
    {
        if (!AdminSupportResponseFactory.HasSuperAdmin(context))
        {
            return AdminSupportResponseFactory.Problem(context, 403,
                TicketReasonCode.RedactionSuperAdminOnly,
                "super_admin permission required for redaction.");
        }

        var actorId = AdminSupportResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return AdminSupportResponseFactory.Problem(context, 401,
                TicketReasonCode.QueueForbidden, "Authentication required.");
        }

        var result = await handler.HandleAsync(
            new RedactMessageCommand(
                TicketId: ticketId,
                MessageId: messageId,
                SuperAdminActorId: actorId.Value,
                ReasonNote: body?.reason_note ?? string.Empty,
                OriginatingRedactionRequestTicketId: body?.originating_redaction_request_ticket_id,
                RequestingCustomerId: body?.requesting_customer_id),
            ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.RedactionRequestAlreadyRedacted => 409,
                TicketReasonCode.VersionConflict => 409,
                TicketReasonCode.RedactionMessageNotRedactable => 422,
                TicketReasonCode.RedactionRequestMessageNotInOriginatingTicket => 422,
                TicketReasonCode.RedactionReasonRequired => 400,
                _ => 400,
            };
            return AdminSupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "Message redaction rejected.", result.Detail);
        }

        return Results.Ok(new { redacted_at_utc = result.RedactedAtUtc });
    }
}
