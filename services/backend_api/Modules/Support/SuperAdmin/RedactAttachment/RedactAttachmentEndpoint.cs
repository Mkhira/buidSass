using BackendApi.Modules.Support.Agent;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.SuperAdmin.RedactAttachment;

public static class RedactAttachmentEndpoint
{
    public static IEndpointRouteBuilder MapRedactAttachmentEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{ticketId:guid}/attachments/{attachmentId:guid}/redact", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    public sealed record RedactAttachmentRequest(string reason_note);

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        Guid attachmentId,
        [FromBody] RedactAttachmentRequest body,
        HttpContext context,
        [FromServices] RedactAttachmentHandler handler,
        CancellationToken ct)
    {
        // FR-012a — super_admin only; lead is explicitly rejected.
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
            new RedactAttachmentCommand(
                TicketId: ticketId,
                AttachmentId: attachmentId,
                SuperAdminActorId: actorId.Value,
                ReasonNote: body?.reason_note ?? string.Empty),
            ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.RedactionAttachmentAlreadyRedacted => 409,
                TicketReasonCode.VersionConflict => 409,
                TicketReasonCode.RedactionReasonRequired => 400,
                _ => 400,
            };
            return AdminSupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "Attachment redaction rejected.", result.Detail);
        }

        return Results.Ok(new { redacted_at_utc = result.RedactedAtUtc });
    }
}
