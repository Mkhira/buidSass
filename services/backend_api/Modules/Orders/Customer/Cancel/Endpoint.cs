using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Inventory.Internal.Movements.Return;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using BackendApi.Modules.Orders.Customer.Common;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Orders.Customer.Cancel;

public sealed record CancelRequest(string? Reason);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapCancelEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/cancel", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    /// <summary>FR-004 / FR-022. Policy-enforced cancellation. Authorized payment → voided +
    /// order cancelled; captured payment within window → cancellation_pending (refund flow);
    /// shipment exists → 409.</summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        CancelRequest? body,
        HttpContext context,
        OrdersDbContext db,
        CancellationPolicy policy,
        InventoryDbContext inventoryDb,
        AtsCalculator atsCalculator,
        BucketMapper bucketMapper,
        AvailabilityEventEmitter availabilityEventEmitter,
        IAuditEventPublisher auditEventPublisher,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var accountId = CustomerOrdersResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return CustomerOrdersResponseFactory.Problem(context, 401, "orders.requires_auth", "Auth required", "");
        }

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null || order.AccountId != accountId)
        {
            return CustomerOrdersResponseFactory.Problem(context, 404, "order.not_found", "Order not found", "");
        }

        if (string.Equals(order.OrderState, OrderSm.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new { orderId = order.Id, orderState = order.OrderState, message = "Already cancelled." });
        }
        if (string.Equals(order.OrderState, OrderSm.CancellationPending, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new { orderId = order.Id, orderState = order.OrderState, message = "Cancellation already in progress." });
        }

        var shipmentExists = await db.Shipments.AnyAsync(s => s.OrderId == id, ct);
        var nowUtc = DateTimeOffset.UtcNow;
        var decision = await policy.EvaluateAsync(
            order.MarketCode, order.PaymentState, order.PlacedAt, shipmentExists, nowUtc, ct);
        if (!decision.Allowed)
        {
            return CustomerOrdersResponseFactory.Problem(
                context,
                decision.ReasonCode == "order.cancel.shipment_exists" ? 409 : decision.ReasonCode == "order.cancel.window_expired" ? 400 : 409,
                decision.ReasonCode!,
                "Cancellation denied",
                "");
        }

        var capturedPayment = PaymentSm.IsCaptured(order.PaymentState);
        var fromOrderState = order.OrderState;
        var fromPaymentState = order.PaymentState;
        var fromFulfillmentState = order.FulfillmentState;

        if (capturedPayment)
        {
            // Refund flow — order pends until refund completes (spec 013 owns the refund tx).
            order.OrderState = OrderSm.CancellationPending;
            order.UpdatedAt = nowUtc;
            db.StateTransitions.Add(NewTransition(order.Id, OrderStateTransition.MachineOrder,
                fromOrderState, OrderSm.CancellationPending, accountId, "customer.cancel", body?.Reason, nowUtc));
            db.Outbox.Add(NewOutbox(order, "order.cancellation_pending", new { reason = body?.Reason }));
        }
        else
        {
            // Authorized / pending — cancel synchronously: order cancelled, payment voided
            // (or refunded if it had captured under PartiallyRefunded? no — captured branched above),
            // fulfillment cancelled to keep state consistency.
            order.OrderState = OrderSm.Cancelled;
            order.CancelledAt = nowUtc;
            order.PaymentState = PaymentSm.Voided;
            order.FulfillmentState = FulfillmentSm.Cancelled;
            order.UpdatedAt = nowUtc;

            db.StateTransitions.Add(NewTransition(order.Id, OrderStateTransition.MachineOrder,
                fromOrderState, OrderSm.Cancelled, accountId, "customer.cancel", body?.Reason, nowUtc));
            db.StateTransitions.Add(NewTransition(order.Id, OrderStateTransition.MachinePayment,
                fromPaymentState, PaymentSm.Voided, accountId, "customer.cancel", body?.Reason, nowUtc));
            db.StateTransitions.Add(NewTransition(order.Id, OrderStateTransition.MachineFulfillment,
                fromFulfillmentState, FulfillmentSm.Cancelled, accountId, "customer.cancel", body?.Reason, nowUtc));
            db.Outbox.Add(NewOutbox(order, "order.cancelled", new { reason = body?.Reason }));
            db.Outbox.Add(NewOutbox(order, "payment.voided", new { reason = body?.Reason }));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CustomerOrdersResponseFactory.Problem(context, 409, "order.concurrency_conflict", "Concurrent modification", "");
        }

        // B5 fix: spec 011 US 2 — "*when* cancel fires, *then* … reservations release."
        // For the synchronous-cancel path (authorized / pending payment) we post a return
        // movement against every prior `kind='sale'` movement for this order so the on-hand
        // stock is restored. Best-effort: a failure here doesn't roll back the order
        // cancellation (the customer-facing intent is honoured), but emits an outbox event
        // so ops can reconcile.
        if (!capturedPayment)
        {
            await ReleaseInventoryAsync(
                order, accountId.Value, db, inventoryDb,
                atsCalculator, bucketMapper, availabilityEventEmitter, auditEventPublisher,
                loggerFactory.CreateLogger("Orders.Cancel.InventoryRelease"), ct);
        }

        return Results.Ok(new
        {
            orderId = order.Id,
            orderState = order.OrderState,
            paymentState = order.PaymentState,
            fulfillmentState = order.FulfillmentState,
        });
    }

    private static async Task ReleaseInventoryAsync(
        Order order,
        Guid actorAccountId,
        OrdersDbContext ordersDb,
        InventoryDbContext inventoryDb,
        AtsCalculator atsCalculator,
        BucketMapper bucketMapper,
        AvailabilityEventEmitter availabilityEventEmitter,
        IAuditEventPublisher auditEventPublisher,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            // Sum the prior sale movements per (product, warehouse, batch). Spec 008's Convert
            // handler stamps SourceKind='order' + SourceId=orderId; that's our back-reference.
            var saleRows = await inventoryDb.InventoryMovements.AsNoTracking()
                .Where(m => m.SourceKind == "order" && m.SourceId == order.Id && m.Kind == "sale")
                .Select(m => new { m.ProductId, m.WarehouseId, m.BatchId, m.Delta })
                .ToListAsync(ct);
            if (saleRows.Count == 0)
            {
                logger.LogInformation(
                    "orders.cancel.inventory_release.no_sales orderId={OrderId} — nothing to reverse.", order.Id);
                return;
            }
            // Sale movements have negative delta; the return submits the absolute value.
            var items = saleRows
                .GroupBy(r => (r.ProductId, r.WarehouseId, r.BatchId))
                .Select(g => new ReturnMovementItem(
                    ProductId: g.Key.ProductId,
                    WarehouseId: g.Key.WarehouseId,
                    BatchId: g.Key.BatchId,
                    Qty: -g.Sum(x => x.Delta)))
                .Where(i => i.Qty > 0)
                .ToArray();
            if (items.Length == 0) return;

            var result = await Handler.HandleAsync(
                new ReturnMovementRequest(
                    OrderId: order.Id,
                    AccountId: actorAccountId,
                    ReasonCode: "orders.cancelled",
                    Items: items),
                inventoryDb,
                atsCalculator,
                bucketMapper,
                availabilityEventEmitter,
                auditEventPublisher,
                actorAccountId,
                ct);
            if (!result.IsSuccess)
            {
                logger.LogWarning(
                    "orders.cancel.inventory_release.failed orderId={OrderId} reason={Reason}",
                    order.Id, result.ReasonCode);
                ordersDb.Outbox.Add(new OrdersOutboxEntry
                {
                    EventType = "fulfillment.inventory_release_failed",
                    AggregateId = order.Id,
                    PayloadJson = JsonSerializer.Serialize(new { orderId = order.Id, reason = result.ReasonCode }),
                    CommittedAt = DateTimeOffset.UtcNow,
                });
                await ordersDb.SaveChangesAsync(ct);
            }
            else
            {
                logger.LogInformation(
                    "orders.cancel.inventory_release.ok orderId={OrderId} movements={Count}",
                    order.Id, items.Length);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "orders.cancel.inventory_release.threw orderId={OrderId}", order.Id);
        }
    }

    private static OrderStateTransition NewTransition(Guid orderId, string machine, string from, string to, Guid? actor, string trigger, string? reason, DateTimeOffset nowUtc) =>
        new()
        {
            OrderId = orderId,
            Machine = machine,
            FromState = from,
            ToState = to,
            ActorAccountId = actor,
            Trigger = trigger,
            Reason = reason,
            OccurredAt = nowUtc,
        };

    private static OrdersOutboxEntry NewOutbox(Entities.Order order, string eventType, object payload) =>
        new()
        {
            EventType = eventType,
            AggregateId = order.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                payload,
            }),
            CommittedAt = DateTimeOffset.UtcNow,
            DispatchedAt = null,
        };
}
