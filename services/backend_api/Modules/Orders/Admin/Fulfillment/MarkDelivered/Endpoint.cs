using System.Text.Json;
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

namespace BackendApi.Modules.Orders.Admin.Fulfillment.MarkDelivered;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapMarkDeliveredEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/fulfillment/mark-delivered", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("orders.fulfillment.write");
        return builder;
    }

    /// <summary>
    /// FR-026 / SC-008. Delivery confirmation. For COD orders, additionally captures payment
    /// (PaymentSm: pending_cod → captured) and emits payment.captured for spec 012's invoice
    /// trigger (FR-015).
    /// </summary>
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
        var fromFulfillmentState = order.FulfillmentState;
        if (!FulfillmentSm.IsValidTransition(fromFulfillmentState, FulfillmentSm.Delivered))
        {
            return AdminOrdersResponseFactory.Problem(context, 409, "order.fulfillment.not_ready",
                "Cannot mark delivered from current fulfillment state", "",
                new Dictionary<string, object?> { ["currentState"] = fromFulfillmentState });
        }

        var nowUtc = DateTimeOffset.UtcNow;
        order.FulfillmentState = FulfillmentSm.Delivered;
        order.DeliveredAt = nowUtc;
        order.UpdatedAt = nowUtc;
        db.StateTransitions.Add(FulfillmentOps.NewTransition(
            order.Id, OrderStateTransition.MachineFulfillment, fromFulfillmentState, FulfillmentSm.Delivered,
            actor, "admin.mark_delivered", null, nowUtc));
        db.Outbox.Add(FulfillmentOps.NewOutbox(order, "fulfillment.delivered"));

        // FR-026 / SC-008: COD delivery → payment captured.
        var paymentChanged = false;
        var fromPaymentState = order.PaymentState;
        if (string.Equals(order.PaymentState, PaymentSm.PendingCod, StringComparison.OrdinalIgnoreCase)
            && PaymentSm.IsValidTransition(order.PaymentState, PaymentSm.Captured))
        {
            order.PaymentState = PaymentSm.Captured;
            paymentChanged = true;
            db.StateTransitions.Add(FulfillmentOps.NewTransition(
                order.Id, OrderStateTransition.MachinePayment, fromPaymentState, PaymentSm.Captured,
                actor, "admin.mark_delivered.cod_capture", null, nowUtc));
            db.Outbox.Add(new OrdersOutboxEntry
            {
                EventType = "payment.captured",
                AggregateId = order.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    orderId = order.Id,
                    orderNumber = order.OrderNumber,
                    capturedAmountMinor = order.GrandTotalMinor,
                    currency = order.Currency,
                    capturedAt = nowUtc,
                    method = "cod",
                }),
                CommittedAt = nowUtc,
                DispatchedAt = null,
            });
        }

        // Sync latest shipment.
        var latest = await db.Shipments.Where(s => s.OrderId == id)
            .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct);
        if (latest is not null)
        {
            latest.State = Shipment.StateDelivered;
            latest.DeliveredAt = nowUtc;
        }

        await db.SaveChangesAsync(ct);

        await FulfillmentOps.EmitAdminAuditAsync(auditPublisher, order.Id, actor.Value,
            "orders.fulfillment.mark_delivered",
            new { fulfillmentState = fromFulfillmentState, paymentState = fromPaymentState },
            new { fulfillmentState = order.FulfillmentState, paymentState = order.PaymentState },
            null, ct);

        return Results.Ok(new
        {
            orderId = order.Id,
            fulfillmentState = order.FulfillmentState,
            paymentState = order.PaymentState,
            paymentCaptured = paymentChanged,
        });
    }
}
