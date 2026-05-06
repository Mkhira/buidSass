using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Agent.ListAgentQueue;

public static class ListAgentQueueEndpoint
{
    public static IEndpointRouteBuilder MapListAgentQueueEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/queue", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        [FromServices] ListAgentQueueHandler handler,
        CancellationToken ct,
        string? market_code = null,
        string? category = null,
        string? priority = null,
        string? state = null,
        Guid? assigned_agent_id = null,
        string? sla_breach_status = null,
        string? linked_entity_kind = null,
        DateTimeOffset? created_after = null,
        DateTimeOffset? created_before = null,
        int? skip = null,
        int? take = null)
    {
        if (!AdminSupportResponseFactory.HasAgentLevelAccess(context))
        {
            return AdminSupportResponseFactory.Problem(context, 403,
                TicketReasonCode.QueueForbidden,
                "support.agent permission required.");
        }

        var query = new ListAgentQueueQuery(
            MarketCode: market_code,
            Categories: SplitCsv(category),
            Priorities: SplitCsv(priority),
            States: SplitCsv(state),
            AssignedAgentId: assigned_agent_id,
            SlaBreachStatus: sla_breach_status,
            LinkedEntityKind: linked_entity_kind,
            CreatedAfter: created_after,
            CreatedBefore: created_before,
            Skip: skip ?? 0,
            Take: take ?? 50);

        var result = await handler.HandleAsync(query, ct);
        return Results.Ok(new { items = result.Items, total = result.Total });
    }

    private static string[]? SplitCsv(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? null
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
