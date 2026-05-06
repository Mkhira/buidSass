using BackendApi.Modules.Support.Agent;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Lead.OverrideSlaTargets;

public static class OverrideSlaTargetsEndpoint
{
    public static IEndpointRouteBuilder MapOverrideSlaTargetsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{ticketId:guid}/sla-override", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    public sealed record OverrideSlaTargetsRequest(
        int first_response_target_minutes,
        int resolution_target_minutes,
        string justification_note);

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        [FromBody] OverrideSlaTargetsRequest body,
        HttpContext context,
        [FromServices] OverrideSlaTargetsHandler handler,
        CancellationToken ct)
    {
        if (!AdminSupportResponseFactory.HasLeadPermission(context)
            && !AdminSupportResponseFactory.HasSuperAdmin(context))
        {
            return AdminSupportResponseFactory.Problem(context, 403,
                TicketReasonCode.QueueForbidden,
                "support.lead permission required.");
        }

        var actorId = AdminSupportResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return AdminSupportResponseFactory.Problem(context, 401,
                TicketReasonCode.QueueForbidden,
                "Authentication required.");
        }

        if (body is null)
        {
            return AdminSupportResponseFactory.Problem(context, 400,
                TicketReasonCode.SlaOverrideJustificationRequired,
                "Request body is required.");
        }

        var result = await handler.HandleAsync(
            new OverrideSlaTargetsCommand(
                TicketId: ticketId,
                LeadActorId: actorId.Value,
                NewFirstResponseTargetMinutes: body.first_response_target_minutes,
                NewResolutionTargetMinutes: body.resolution_target_minutes,
                JustificationNote: body.justification_note ?? string.Empty),
            ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.ClosedTerminal => 409,
                TicketReasonCode.VersionConflict => 409,
                TicketReasonCode.SlaOverrideJustificationRequired => 400,
                TicketReasonCode.SlaOverrideResolutionMustExceedFirstResponse => 400,
                _ => 400,
            };
            return AdminSupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "SLA override rejected.", result.Detail);
        }

        return Results.Ok(new
        {
            prior_first_response_target_minutes = result.PriorFirstResponseTargetMinutes,
            prior_resolution_target_minutes = result.PriorResolutionTargetMinutes,
            new_first_response_target_minutes = result.NewFirstResponseTargetMinutes,
            new_resolution_target_minutes = result.NewResolutionTargetMinutes,
            new_first_response_due_utc = result.NewFirstResponseDueUtc,
            new_resolution_due_utc = result.NewResolutionDueUtc,
        });
    }
}
