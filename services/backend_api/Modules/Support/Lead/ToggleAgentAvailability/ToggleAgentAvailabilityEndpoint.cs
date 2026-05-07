using BackendApi.Modules.Support.Agent;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Lead.ToggleAgentAvailability;

public static class ToggleAgentAvailabilityEndpoint
{
    public static IEndpointRouteBuilder MapToggleAgentAvailabilityEndpoint(this IEndpointRouteBuilder builder)
    {
        // Note: this is an /api/admin/agent-availability/toggle endpoint per
        // T123. We expose it on the support-tickets group for now using a
        // sibling-prefix route; the endpoint group remapping lives in
        // SupportModule.cs's MapPhase10Endpoints.
        builder.MapPost("/agent-availability/toggle", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    public sealed record ToggleAgentAvailabilityRequest(
        Guid agent_id,
        string market_code,
        bool is_on_call);

    private static async Task<IResult> HandleAsync(
        [FromBody] ToggleAgentAvailabilityRequest body,
        HttpContext context,
        [FromServices] ToggleAgentAvailabilityHandler handler,
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
                TicketReasonCode.QueueForbidden, "Authentication required.");
        }

        if (body is null)
        {
            return AdminSupportResponseFactory.Problem(context, 400,
                TicketReasonCode.MarketInvalid, "Request body is required.");
        }

        var result = await handler.HandleAsync(
            new ToggleAgentAvailabilityCommand(
                AgentId: body.agent_id,
                MarketCode: body.market_code ?? string.Empty,
                IsOnCall: body.is_on_call,
                LeadActorId: actorId.Value),
            ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.VersionConflict => 409,
                TicketReasonCode.MarketInvalid => 400,
                TicketReasonCode.TargetAgentNotInMarket => 400,
                _ => 400,
            };
            return AdminSupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "Availability toggle rejected.", result.Detail);
        }

        return Results.Ok(new
        {
            agent_id = body.agent_id,
            market_code = body.market_code,
            is_on_call = result.IsOnCall,
            last_toggled_at_utc = result.LastToggledAtUtc,
        });
    }
}
