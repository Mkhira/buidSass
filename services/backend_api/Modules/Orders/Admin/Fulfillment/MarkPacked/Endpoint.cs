using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Orders.Admin.Common;
using BackendApi.Modules.Orders.Admin.Fulfillment.Common;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Admin.Fulfillment.MarkPacked;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapMarkPackedEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/fulfillment/mark-packed", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("orders.fulfillment.write");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        OrdersDbContext db,
        IAuditEventPublisher auditPublisher,
        CancellationToken ct)
    {
        var actor = AdminOrdersResponseFactory.ResolveActorAccountId(context);
        if (actor is null || actor == Guid.Empty)
        {
            return AdminOrdersResponseFactory.Problem(context, 401, "orders.actor_required", "Actor required", "");
        }
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return AdminOrdersResponseFactory.Problem(context, 404, "order.not_found", "Order not found", "");
        }
        var fromState = order.FulfillmentState;
        if (!FulfillmentSm.IsValidTransition(fromState, FulfillmentSm.Packed))
        {
            return AdminOrdersResponseFactory.Problem(context, 409, "order.fulfillment.not_ready",
                "Cannot mark packed from current fulfillment state", "",
                new Dictionary<string, object?> { ["currentState"] = fromState });
        }

        var nowUtc = DateTimeOffset.UtcNow;
        order.FulfillmentState = FulfillmentSm.Packed;
        order.UpdatedAt = nowUtc;
        db.StateTransitions.Add(FulfillmentOps.NewTransition(
            order.Id, OrderStateTransition.MachineFulfillment, fromState, FulfillmentSm.Packed,
            actor, "admin.mark_packed", null, nowUtc));
        db.Outbox.Add(FulfillmentOps.NewOutbox(order, "fulfillment.packed"));
        await db.SaveChangesAsync(ct);

        await FulfillmentOps.EmitAdminAuditAsync(auditPublisher, order.Id, actor.Value,
            "orders.fulfillment.mark_packed",
            new { fulfillmentState = fromState }, new { fulfillmentState = order.FulfillmentState }, null, ct);

        return Results.Ok(new { orderId = order.Id, fulfillmentState = order.FulfillmentState });
    }
}
