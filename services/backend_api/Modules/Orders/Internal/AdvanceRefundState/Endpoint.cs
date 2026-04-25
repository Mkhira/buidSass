using System.Text.Json;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Orders.Internal.AdvanceRefundState;

public sealed record ReturnedLineDelta(Guid OrderLineId, int DeltaQty);

public sealed record AdvanceRefundStateRequest(
    string EventType,
    Guid? ReturnRequestId,
    Guid? RefundId,
    long RefundedAmountMinor,
    IReadOnlyList<ReturnedLineDelta>? ReturnedLineQtys);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdvanceRefundStateEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/advance-refund-state", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .WithMetadata(new BackendApi.Modules.Identity.Authorization.Filters.RequirePermissionMetadata(
                "orders.internal.advance_refund", null));
        return builder;
    }

    /// <summary>
    /// FR-016 / F4. Refund-state seam invoked by spec 013's <c>returns_outbox</c> dispatcher.
    /// Idempotent on (orderId, eventType, returnRequestId, refundId).
    ///
    /// Semantics (per orders-contract.md):
    ///   • return.submitted          → refund_state none → requested
    ///   • return.rejected           → refund_state requested → none (when no other open RMAs;
    ///                                  caller asserts via dispatcher logic)
    ///   • refund.completed / refund.manual_confirmed →
    ///         (a) increment order_lines.returned_qty by each deltaQty;
    ///         (b) compare cumulative refunded to captured total → advance refund_state
    ///             to partial / full;
    ///         (c) emit payment.partially_refunded or payment.refunded outbox row.
    ///
    /// Errors: 409 order.refund.over_refund_blocked if cumulative refund &gt; captured;
    ///         409 order.line.returned_qty_exceeds_delivered if any line crosses qty - cancelled.
    /// </summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        AdvanceRefundStateRequest body,
        HttpContext context,
        OrdersDbContext db,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Orders.AdvanceRefundState");
        if (string.IsNullOrWhiteSpace(body.EventType))
        {
            return Problem(context, 400, "order.refund.invalid_request", "eventType is required");
        }

        var order = await db.Orders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return Problem(context, 404, "order.not_found", "Order not found");
        }

        // Idempotency: previous deliveries may already have stored a transition row with the
        // same (returnRequestId, refundId, eventType) reason — skip in that case.
        var idempotencyKey = $"event={body.EventType} returnRequestId={body.ReturnRequestId} refundId={body.RefundId}";
        var alreadySeen = await db.StateTransitions
            .AnyAsync(t => t.OrderId == id
                && t.Machine == OrderStateTransition.MachineRefund
                && t.Reason == idempotencyKey, ct);
        if (alreadySeen)
        {
            return Results.Ok(new
            {
                orderId = order.Id,
                refundState = order.RefundState,
                deduped = true,
            });
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var fromRefund = order.RefundState;
        string? targetRefund = null;
        string? paymentEvent = null;

        switch (body.EventType.ToLowerInvariant())
        {
            case "return.submitted":
                if (string.Equals(order.RefundState, RefundSm.None, StringComparison.OrdinalIgnoreCase))
                {
                    targetRefund = RefundSm.Requested;
                }
                break;
            case "return.rejected":
                // Caller asserts no other open RMA remains.
                if (string.Equals(order.RefundState, RefundSm.Requested, StringComparison.OrdinalIgnoreCase))
                {
                    targetRefund = RefundSm.None;
                }
                break;
            case "refund.completed":
            case "refund.manual_confirmed":
                if (body.RefundId is null || body.RefundedAmountMinor < 0)
                {
                    return Problem(context, 400, "order.refund.invalid_request",
                        "refundId and non-negative refundedAmountMinor are required for refund events");
                }
                // Apply per-line returned_qty deltas with bound checks.
                if (body.ReturnedLineQtys is { Count: > 0 } lineDeltas)
                {
                    foreach (var d in lineDeltas)
                    {
                        var line = order.Lines.FirstOrDefault(l => l.Id == d.OrderLineId);
                        if (line is null)
                        {
                            return Problem(context, 404, "order.refund.line_not_found",
                                $"OrderLine {d.OrderLineId} not found on order {order.Id}");
                        }
                        var newReturned = line.ReturnedQty + d.DeltaQty;
                        if (newReturned + line.CancelledQty > line.Qty)
                        {
                            return Problem(context, 409, "order.line.returned_qty_exceeds_delivered",
                                $"Line {line.Id}: returned ({newReturned}) + cancelled ({line.CancelledQty}) > qty ({line.Qty})");
                        }
                        line.ReturnedQty = newReturned;
                    }
                }
                // Cumulative refund vs captured total — over-refund guard.
                var cumulativeBefore = await SumPriorRefundsAsync(db, id, ct);
                var cumulativeAfter = cumulativeBefore + body.RefundedAmountMinor;
                if (cumulativeAfter > order.GrandTotalMinor)
                {
                    return Problem(context, 409, "order.refund.over_refund_blocked",
                        $"Cumulative refund {cumulativeAfter} would exceed captured total {order.GrandTotalMinor}");
                }
                if (cumulativeAfter >= order.GrandTotalMinor)
                {
                    targetRefund = RefundSm.Full;
                    paymentEvent = "payment.refunded";
                    if (string.Equals(order.PaymentState, PaymentSm.Captured, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(order.PaymentState, PaymentSm.PartiallyRefunded, StringComparison.OrdinalIgnoreCase))
                    {
                        order.PaymentState = PaymentSm.Refunded;
                    }
                }
                else if (cumulativeAfter > 0)
                {
                    targetRefund = RefundSm.Partial;
                    paymentEvent = "payment.partially_refunded";
                    if (string.Equals(order.PaymentState, PaymentSm.Captured, StringComparison.OrdinalIgnoreCase))
                    {
                        order.PaymentState = PaymentSm.PartiallyRefunded;
                    }
                }
                break;
            default:
                return Problem(context, 400, "order.refund.invalid_event",
                    $"Unknown eventType '{body.EventType}'");
        }

        if (targetRefund is null)
        {
            return Results.Ok(new
            {
                orderId = order.Id,
                refundState = order.RefundState,
                noop = true,
            });
        }

        if (!RefundSm.IsValidTransition(fromRefund, targetRefund))
        {
            logger.LogWarning(
                "orders.advance_refund_state.invalid_transition orderId={OrderId} from={From} to={To}",
                order.Id, fromRefund, targetRefund);
            return Problem(context, 409, "order.state.illegal_transition",
                $"Refund state transition {fromRefund} → {targetRefund} is not allowed");
        }

        order.RefundState = targetRefund;
        order.UpdatedAt = nowUtc;
        db.StateTransitions.Add(new OrderStateTransition
        {
            OrderId = order.Id,
            Machine = OrderStateTransition.MachineRefund,
            FromState = fromRefund,
            ToState = targetRefund,
            ActorAccountId = null,
            Trigger = $"returns.{body.EventType}",
            Reason = idempotencyKey,
            ContextJson = JsonSerializer.Serialize(new
            {
                returnRequestId = body.ReturnRequestId,
                refundId = body.RefundId,
                refundedAmountMinor = body.RefundedAmountMinor,
            }),
            OccurredAt = nowUtc,
        });
        if (paymentEvent is not null)
        {
            db.Outbox.Add(new OrdersOutboxEntry
            {
                EventType = paymentEvent,
                AggregateId = order.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    orderId = order.Id,
                    orderNumber = order.OrderNumber,
                    refundedAmountMinor = body.RefundedAmountMinor,
                    refundId = body.RefundId,
                }),
                CommittedAt = nowUtc,
            });
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            orderId = order.Id,
            refundState = order.RefundState,
            paymentState = order.PaymentState,
        });
    }

    private static async Task<long> SumPriorRefundsAsync(OrdersDbContext db, Guid orderId, CancellationToken ct)
    {
        // Sum prior refund.completed / refund.manual_confirmed amounts from the transitions table.
        var transitions = await db.StateTransitions.AsNoTracking()
            .Where(t => t.OrderId == orderId
                && t.Machine == OrderStateTransition.MachineRefund
                && (t.Trigger == "returns.refund.completed" || t.Trigger == "returns.refund.manual_confirmed"))
            .Select(t => t.ContextJson)
            .ToListAsync(ct);
        long total = 0;
        foreach (var ctx in transitions)
        {
            if (string.IsNullOrWhiteSpace(ctx)) continue;
            try
            {
                using var doc = JsonDocument.Parse(ctx);
                if (doc.RootElement.TryGetProperty("refundedAmountMinor", out var amt) && amt.TryGetInt64(out var v))
                {
                    total += v;
                }
            }
            catch (JsonException)
            {
                // Defensive: skip malformed context — caller will surface in logs.
            }
        }
        return total;
    }

    private static IResult Problem(HttpContext context, int status, string code, string detail)
    {
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = status,
            Title = "Refund advance error",
            Detail = detail,
            Type = $"https://errors.dental-commerce/orders/{code}",
            Instance = context.Request.Path,
        };
        problem.Extensions["reasonCode"] = code;
        return Results.Json(problem, statusCode: status, contentType: "application/problem+json");
    }
}
