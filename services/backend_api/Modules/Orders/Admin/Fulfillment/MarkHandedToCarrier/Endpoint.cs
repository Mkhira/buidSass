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

namespace BackendApi.Modules.Orders.Admin.Fulfillment.MarkHandedToCarrier;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapMarkHandedToCarrierEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/fulfillment/mark-handed-to-carrier", HandleAsync)
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
        // Require at least one shipment row before allowing this transition.
        var hasShipment = await db.Shipments.AnyAsync(s => s.OrderId == id, ct);
        if (!hasShipment)
        {
            return AdminOrdersResponseFactory.Problem(context, 409, "order.fulfillment.no_shipment",
                "Create a shipment row before marking handed-to-carrier", "");
        }
        var fromState = order.FulfillmentState;
        if (!FulfillmentSm.IsValidTransition(fromState, FulfillmentSm.HandedToCarrier))
        {
            return AdminOrdersResponseFactory.Problem(context, 409, "order.fulfillment.not_ready",
                "Cannot transition to handed-to-carrier from current fulfillment state", "",
                new Dictionary<string, object?> { ["currentState"] = fromState });
        }

        var nowUtc = DateTimeOffset.UtcNow;
        order.FulfillmentState = FulfillmentSm.HandedToCarrier;
        order.UpdatedAt = nowUtc;
        // Sync the latest shipment's state too, so the per-shipment trail mirrors the aggregate.
        var latest = await db.Shipments
            .Where(s => s.OrderId == id)
            .OrderByDescending(s => s.CreatedAt)
            .FirstAsync(ct);
        latest.State = Shipment.StateHandedToCarrier;
        latest.HandedToCarrierAt = nowUtc;

        db.StateTransitions.Add(FulfillmentOps.NewTransition(
            order.Id, OrderStateTransition.MachineFulfillment, fromState, FulfillmentSm.HandedToCarrier,
            actor, "admin.mark_handed_to_carrier", null, nowUtc));
        db.Outbox.Add(FulfillmentOps.NewOutbox(order, "fulfillment.shipped", new
        {
            shipmentId = latest.Id,
            trackingNumber = latest.TrackingNumber,
            providerId = latest.ProviderId,
        }));
        await db.SaveChangesAsync(ct);

        await FulfillmentOps.EmitAdminAuditAsync(auditPublisher, order.Id, actor.Value,
            "orders.fulfillment.mark_handed_to_carrier",
            new { fulfillmentState = fromState }, new { fulfillmentState = order.FulfillmentState }, null, ct);

        return Results.Ok(new
        {
            orderId = order.Id,
            fulfillmentState = order.FulfillmentState,
            shipmentId = latest.Id,
        });
    }
}
