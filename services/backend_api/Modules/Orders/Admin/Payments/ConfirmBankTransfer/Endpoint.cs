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

namespace BackendApi.Modules.Orders.Admin.Payments.ConfirmBankTransfer;

public sealed record ConfirmBankTransferRequest(string Reference, DateTimeOffset ReceivedAt);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminConfirmBankTransferEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/payments/confirm-bank-transfer", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("orders.payment.write");
        return builder;
    }

    /// <summary>FR-025. Bank-transfer reconciliation: pending_bank_transfer → captured.
    /// Emits payment.captured for spec 012's invoice issuance.</summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        ConfirmBankTransferRequest body,
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
        if (string.IsNullOrWhiteSpace(body.Reference))
        {
            return AdminOrdersResponseFactory.Problem(context, 400, "orders.payment.reference_required",
                "Bank transfer reference is required", "");
        }
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return AdminOrdersResponseFactory.Problem(context, 404, "order.not_found", "Order not found", "");
        }
        if (!string.Equals(order.PaymentState, PaymentSm.PendingBankTransfer, StringComparison.OrdinalIgnoreCase))
        {
            return AdminOrdersResponseFactory.Problem(context, 409,
                "order.payment.not_in_pending_bank_transfer",
                $"Cannot confirm bank transfer from state '{order.PaymentState}'", "");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var fromState = order.PaymentState;
        order.PaymentState = PaymentSm.Captured;
        order.UpdatedAt = nowUtc;
        db.StateTransitions.Add(FulfillmentOps.NewTransition(
            order.Id, OrderStateTransition.MachinePayment, fromState, PaymentSm.Captured,
            actor, "admin.confirm_bank_transfer", $"reference={body.Reference} receivedAt={body.ReceivedAt:o}", nowUtc));
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
                method = "bank_transfer",
                reference = body.Reference,
                receivedAt = body.ReceivedAt,
            }),
            CommittedAt = nowUtc,
        });
        await db.SaveChangesAsync(ct);

        await FulfillmentOps.EmitAdminAuditAsync(auditPublisher, order.Id, actor.Value,
            "orders.payment.confirm_bank_transfer",
            new { paymentState = fromState },
            new { paymentState = order.PaymentState, reference = body.Reference, receivedAt = body.ReceivedAt },
            null, ct);

        return Results.Ok(new
        {
            orderId = order.Id,
            paymentState = order.PaymentState,
            reference = body.Reference,
        });
    }
}
