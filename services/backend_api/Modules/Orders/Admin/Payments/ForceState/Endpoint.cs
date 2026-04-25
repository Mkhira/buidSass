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

namespace BackendApi.Modules.Orders.Admin.Payments.ForceState;

public sealed record ForceStateRequest(string ToState, string Reason);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminForceStateEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/payments/force-state", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("orders.payment.write");
        return builder;
    }

    /// <summary>
    /// FR-008 + SC-010. High-privilege admin override of payment state. Reason is mandatory;
    /// every invocation writes an audit row regardless of whether the transition is already
    /// in the canonical PaymentSm table — operations occasionally need to force a state for
    /// reconciliation. The state-machine validation still applies (no path → unknown values).
    /// </summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        ForceStateRequest body,
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
        if (string.IsNullOrWhiteSpace(body.ToState) || string.IsNullOrWhiteSpace(body.Reason))
        {
            return AdminOrdersResponseFactory.Problem(context, 400, "orders.payment.invalid_request",
                "Both toState and reason are required", "");
        }
        if (!PaymentSm.All.Contains(body.ToState))
        {
            return AdminOrdersResponseFactory.Problem(context, 400, "order.state.illegal_transition",
                $"Unknown payment state '{body.ToState}'", "");
        }
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return AdminOrdersResponseFactory.Problem(context, 404, "order.not_found", "Order not found", "");
        }
        var fromState = order.PaymentState;
        var nowUtc = DateTimeOffset.UtcNow;
        order.PaymentState = body.ToState;
        order.UpdatedAt = nowUtc;
        db.StateTransitions.Add(FulfillmentOps.NewTransition(
            order.Id, OrderStateTransition.MachinePayment, fromState, body.ToState,
            actor, "admin.force_state", body.Reason, nowUtc));
        db.Outbox.Add(new OrdersOutboxEntry
        {
            EventType = "payment.admin_forced",
            AggregateId = order.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                fromState,
                toState = body.ToState,
                actor,
                reason = body.Reason,
            }),
            CommittedAt = nowUtc,
        });
        await db.SaveChangesAsync(ct);

        await FulfillmentOps.EmitAdminAuditAsync(auditPublisher, order.Id, actor.Value,
            "orders.payment.force_state",
            new { paymentState = fromState },
            new { paymentState = body.ToState },
            body.Reason, ct);

        return Results.Ok(new { orderId = order.Id, paymentState = order.PaymentState });
    }
}
