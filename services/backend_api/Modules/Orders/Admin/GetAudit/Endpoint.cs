using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Orders.Admin.Common;
using BackendApi.Modules.Orders.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Admin.GetAudit;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminGetAuditEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}/audit", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("orders.read");
        return builder;
    }

    /// <summary>FR-023. Returns the full audit trail for an order: every state-machine
    /// transition + admin mutations from spec 003's audit_log_entries.</summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        OrdersDbContext db,
        BackendApi.Modules.Shared.AppDbContext appDb,
        CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return AdminOrdersResponseFactory.Problem(context, 404, "order.not_found", "Order not found", "");
        }
        var transitions = await db.StateTransitions.AsNoTracking()
            .Where(t => t.OrderId == id)
            .OrderBy(t => t.OccurredAt)
            .Select(t => new
            {
                kind = "state_transition",
                t.Machine,
                t.FromState,
                t.ToState,
                t.OccurredAt,
                t.ActorAccountId,
                t.Trigger,
                t.Reason,
            })
            .ToListAsync(ct);

        // Spec 003 audit_log_entries: cross-schema read (research R12 — same monolith DB).
        var auditEntries = await appDb.Database
            .SqlQueryRaw<AuditRow>(
                """
                SELECT "Id" AS Id, "ActorId" AS ActorId, "ActorRole" AS ActorRole,
                       "Action" AS Action, "EntityType" AS EntityType, "EntityId" AS EntityId,
                       "BeforeJson" AS BeforeJson, "AfterJson" AS AfterJson,
                       "Reason" AS Reason, "OccurredAt" AS OccurredAt
                FROM audit.audit_log_entries
                WHERE "EntityType" = 'orders.order' AND "EntityId" = {0}::text
                ORDER BY "OccurredAt"
                """, id.ToString())
            .ToListAsync(ct);

        return Results.Ok(new
        {
            orderId = order.Id,
            orderNumber = order.OrderNumber,
            transitions,
            adminActions = auditEntries,
        });
    }

    private sealed record AuditRow(
        long Id, Guid ActorId, string ActorRole, string Action, string EntityType, string EntityId,
        string? BeforeJson, string? AfterJson, string? Reason, DateTimeOffset OccurredAt);
}
