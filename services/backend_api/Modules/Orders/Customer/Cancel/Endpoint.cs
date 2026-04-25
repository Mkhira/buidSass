using System.Text.Json;
using BackendApi.Modules.Orders.Customer.Common;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

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

        return Results.Ok(new
        {
            orderId = order.Id,
            orderState = order.OrderState,
            paymentState = order.PaymentState,
            fulfillmentState = order.FulfillmentState,
        });
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
