using BackendApi.Modules.Support.Customer;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Agent.GetTicketAdminDetail;

/// <summary>
/// HTTP endpoint for the admin-side full ticket-detail read (T071).
/// </summary>
public static class GetTicketAdminDetailEndpoint
{
    public static IEndpointRouteBuilder MapGetTicketAdminDetailEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{ticketId:guid}/detail", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        HttpContext context,
        [FromServices] GetTicketAdminDetailHandler handler,
        CancellationToken ct)
    {
        if (!AdminSupportResponseFactory.HasAgentLevelAccess(context))
        {
            return AdminSupportResponseFactory.Problem(context, 403,
                TicketReasonCode.QueueForbidden,
                "support.agent permission required.");
        }
        var actorId = AdminSupportResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return AdminSupportResponseFactory.Problem(context, 401,
                TicketReasonCode.QueueForbidden, "Authentication required.");
        }

        var isSuperAdmin = AdminSupportResponseFactory.HasSuperAdmin(context);
        var isLeadOrSuperAdmin =
            AdminSupportResponseFactory.HasLeadPermission(context)
            || isSuperAdmin;
        var marketCode = SupportResponseFactory.ResolveMarketCode(context);

        var result = await handler.HandleAsync(new GetTicketAdminDetailQuery(
            TicketId: ticketId,
            ActorId: actorId.Value,
            IsLeadOrSuperAdmin: isLeadOrSuperAdmin,
            MarketCode: marketCode,
            IsSuperAdmin: isSuperAdmin), ct);

        if (!result.Success)
        {
            var status = result.ReasonCode == TicketReasonCode.LinkedEntityNotFound ? 404 : 400;
            return AdminSupportResponseFactory.Problem(context, status,
                result.ReasonCode ?? TicketReasonCode.QueueForbidden,
                result.Detail ?? "Ticket detail read failed.");
        }

        return Results.Ok(new
        {
            ticket_id = result.TicketId,
            state = result.State,
            category = result.Category,
            priority = result.Priority,
            subject = result.Subject,
            body = result.Body,
            market_code = result.MarketCode,
            locale = result.Locale,
            assigned_agent_id = result.AssignedAgentId,
            created_at_utc = result.CreatedAtUtc,
            updated_at_utc = result.UpdatedAtUtc,
            resolved_at_utc = result.ResolvedAtUtc,
            closed_at_utc = result.ClosedAtUtc,
            reopen_count = result.ReopenCount,
            sla = new
            {
                first_response_target_minutes = result.FirstResponseTargetMinutesSnapshot,
                resolution_target_minutes = result.ResolutionTargetMinutesSnapshot,
                first_response_due_utc = result.FirstResponseDueUtc,
                resolution_due_utc = result.ResolutionDueUtc,
                breach_acknowledged_at_first_response = result.BreachAcknowledgedAtFirstResponse,
                breach_acknowledged_at_resolution = result.BreachAcknowledgedAtResolution,
            },
            customer = result.Customer,
            linked_entity_preview = result.LinkedEntityPreview,
            messages = result.Messages,
            assignments = result.Assignments,
            links = result.Links,
        });
    }
}
