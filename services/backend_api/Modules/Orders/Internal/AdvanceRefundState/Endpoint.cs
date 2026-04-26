using BackendApi.Modules.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

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
        AdvanceRefundStateService service,
        CancellationToken ct)
    {
        var lines = body.ReturnedLineQtys?
            .Select(l => new OrderRefundReturnedLine(l.OrderLineId, l.DeltaQty))
            .ToList();
        var outcome = await service.AdvanceAsync(id, body.EventType, body.ReturnRequestId, body.RefundId,
            body.RefundedAmountMinor, lines, ct);

        if (!outcome.IsSuccess)
        {
            return Problem(context, outcome.StatusCode, outcome.ReasonCode!, outcome.Detail!);
        }
        if (outcome.Deduped)
        {
            return Results.Ok(new
            {
                orderId = id,
                refundState = outcome.FinalRefundState,
                deduped = true,
            });
        }
        if (outcome.Noop)
        {
            return Results.Ok(new
            {
                orderId = id,
                refundState = outcome.FinalRefundState,
                noop = true,
            });
        }
        return Results.Ok(new
        {
            orderId = id,
            refundState = outcome.FinalRefundState,
            paymentState = outcome.FinalPaymentState,
        });
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
