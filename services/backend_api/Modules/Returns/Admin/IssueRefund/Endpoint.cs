using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Checkout.Primitives.Payment;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Returns.Admin.Common;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Entities;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Returns.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Returns.Admin.IssueRefund;

public sealed record IssueRefundRequest(long? RestockingFeeMinor);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminIssueRefundEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/issue-refund", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("returns.refund.write");
        return builder;
    }

    /// <summary>
    /// FR-007 / FR-014 / FR-022 / SC-005 / SC-006. Issues the refund:
    ///   1. Computes pro-rata amount via <see cref="RefundAmountCalculator"/>.
    ///   2. Verifies cumulative refund + new amount ≤ captured (over-refund guard).
    ///   3. Inserts <c>refund</c> + <c>refund_lines</c> rows; the partial unique index
    ///      <c>IX_returns_refunds_request_active_unique</c> blocks duplicate clicks at the DB.
    ///   4. Calls <see cref="IPaymentGateway.RefundAsync"/> for online orders, OR routes to
    ///      <c>pending_manual_transfer</c> for COD / no-provider orders.
    ///   5. On gateway success → state <c>refunded</c> + emits <c>refund.completed</c>.
    ///      On gateway failure → state <c>refund_failed</c> + emits <c>refund.failed</c>.
    /// </summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        IssueRefundRequest? body,
        HttpContext context,
        ReturnsDbContext db,
        OrdersDbContext ordersDb,
        RefundAmountCalculator calculator,
        IServiceProvider services,
        IAuditEventPublisher auditPublisher,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Returns.IssueRefund");
        var actorId = ReturnsResponseFactory.ResolveAccountId(context);
        if (actorId is null)
        {
            return ReturnsResponseFactory.Problem(context, 401, "returns.requires_auth", "Auth required");
        }

        // Idempotency: if a completed/in-progress/pending_manual_transfer refund already exists
        // for this return, return the same result without calling the gateway again.
        var existing = await db.Refunds.AsNoTracking()
            .FirstOrDefaultAsync(rf => rf.ReturnRequestId == id
                && (rf.State == RefundStateMachine.Completed
                    || rf.State == RefundStateMachine.InProgress
                    || rf.State == RefundStateMachine.PendingManualTransfer
                    || rf.State == RefundStateMachine.Pending), ct);
        if (existing is not null)
        {
            return Results.Ok(new
            {
                id = existing.Id,
                state = existing.State,
                amountMinor = existing.AmountMinor,
                gatewayRef = existing.GatewayRef,
                deduped = true,
            });
        }

        // Begin a single transaction so the refund row + state advance + outbox land atomically.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var r = await db.ReturnRequests.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Return not found.");
        }
        if (!AdminMutation.ValidateTransition(r.State, ReturnStateMachine.Refunded))
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 409, "return.state.illegal_transition",
                $"Cannot issue-refund from state {r.State}.");
        }

        var order = await ordersDb.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == r.OrderId, ct);
        if (order is null)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Order not found.");
        }

        // Determine sellable refundable qty per line: sellable on inspected line OR approved
        // qty on force-refund (skip-physical) path.
        var refundLineInputs = new List<RefundLineInput>();
        foreach (var rl in r.Lines)
        {
            int qtyToRefund;
            if (r.ForceRefund)
            {
                qtyToRefund = rl.ApprovedQty ?? rl.RequestedQty;
            }
            else
            {
                // Only sellable units are refunded; defective units are admin discretion.
                qtyToRefund = rl.SellableQty ?? 0;
            }
            if (qtyToRefund <= 0) continue;
            refundLineInputs.Add(new RefundLineInput(
                ReturnLineId: rl.Id,
                OrderLineId: rl.OrderLineId,
                OriginalQty: rl.OriginalQty,
                QtyToRefund: qtyToRefund,
                UnitPriceMinor: rl.UnitPriceMinor,
                OriginalDiscountMinor: rl.OriginalDiscountMinor,
                OriginalTaxMinor: rl.OriginalTaxMinor,
                TaxRateBp: rl.TaxRateBp));
        }
        if (refundLineInputs.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 400, "refund.no_refundable_units",
                "No sellable / approved units to refund.");
        }

        long restockingFee = body?.RestockingFeeMinor ?? 0L;
        if (restockingFee < 0)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 400, "return.invalid_request",
                "restockingFeeMinor must be non-negative.");
        }
        RefundComputation computation;
        try
        {
            computation = calculator.Compute(refundLineInputs, restockingFee);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 400, "refund.compute_failed", ex.Message);
        }
        if (computation.GrandRefundMinor <= 0)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 400, "refund.amount_zero",
                "Computed refund amount is zero (restocking fee may equal refundable subtotal).");
        }

        // Over-refund guard (FR-022 / SC-006). Sum prior completed/in-progress refunds for
        // this return AND prior refunds for OTHER returns on the same order; reject if the
        // new amount would push cumulative over captured.
        var otherReturnsRefunded = await db.Refunds.AsNoTracking()
            .Where(rf => rf.State == RefundStateMachine.Completed
                || rf.State == RefundStateMachine.InProgress
                || rf.State == RefundStateMachine.PendingManualTransfer)
            .Join(db.ReturnRequests.AsNoTracking().Where(rr => rr.OrderId == r.OrderId && rr.Id != r.Id),
                rf => rf.ReturnRequestId, rr => rr.Id, (rf, _) => rf.AmountMinor)
            .SumAsync(x => (long?)x, ct) ?? 0L;
        if (otherReturnsRefunded + computation.GrandRefundMinor > order.GrandTotalMinor)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 400, "refund.over_refund_blocked",
                $"Cumulative refund {otherReturnsRefunded + computation.GrandRefundMinor} would exceed order total {order.GrandTotalMinor}.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var refund = new Refund
        {
            Id = Guid.NewGuid(),
            ReturnRequestId = r.Id,
            ProviderId = order.PaymentProviderId,
            CapturedTransactionId = order.PaymentProviderTxnId,
            AmountMinor = computation.GrandRefundMinor,
            Currency = order.Currency,
            State = RefundStateMachine.Pending,
            InitiatedAt = nowUtc,
            Attempts = 0,
            RestockingFeeMinor = computation.RestockingFeeMinor,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        foreach (var line in computation.Lines)
        {
            refund.Lines.Add(new RefundLine
            {
                RefundId = refund.Id,
                ReturnLineId = line.ReturnLineId,
                Qty = line.Qty,
                UnitPriceMinor = line.UnitPriceMinor,
                LineSubtotalMinor = line.LineSubtotalMinor,
                LineDiscountMinor = line.LineDiscountMinor,
                LineTaxMinor = line.LineTaxMinor,
                LineAmountMinor = line.LineAmountMinor,
                TaxRateBp = line.TaxRateBp,
            });
        }
        db.Refunds.Add(refund);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505")
        {
            // The partial unique index on Refunds.(ReturnRequestId) WHERE state in active set
            // serialised a concurrent click — re-fetch and dedupe.
            await tx.RollbackAsync(ct);
            var raced = await db.Refunds.AsNoTracking()
                .FirstOrDefaultAsync(rf => rf.ReturnRequestId == id
                    && (rf.State == RefundStateMachine.Completed
                        || rf.State == RefundStateMachine.InProgress
                        || rf.State == RefundStateMachine.PendingManualTransfer
                        || rf.State == RefundStateMachine.Pending), ct);
            if (raced is not null)
            {
                return Results.Ok(new
                {
                    id = raced.Id,
                    state = raced.State,
                    amountMinor = raced.AmountMinor,
                    deduped = true,
                });
            }
            return ReturnsResponseFactory.Problem(context, 409, "refund.duplicate_key", ex.Message);
        }

        // Decide path: COD / no-provider → manual bank transfer; else gateway refund.
        var goManual = string.IsNullOrWhiteSpace(refund.ProviderId)
            || string.IsNullOrWhiteSpace(refund.CapturedTransactionId);

        if (goManual)
        {
            var fromState = r.State;
            refund.State = RefundStateMachine.PendingManualTransfer;
            refund.UpdatedAt = nowUtc;

            db.StateTransitions.Add(new ReturnStateTransition
            {
                ReturnRequestId = r.Id,
                RefundId = refund.Id,
                Machine = ReturnStateTransition.MachineRefund,
                FromState = RefundStateMachine.Pending,
                ToState = RefundStateMachine.PendingManualTransfer,
                ActorAccountId = actorId.Value,
                Trigger = "admin.issue_refund.manual",
                OccurredAt = nowUtc,
            });
            db.Outbox.Add(AdminMutation.NewOutbox("refund.initiated", r.Id, new
            {
                returnRequestId = r.Id,
                refundId = refund.Id,
                amountMinor = refund.AmountMinor,
                manual = true,
            }, nowUtc));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await AdminMutation.PublishAuditAsync(auditPublisher, actorId.Value, "returns.issue_refund.manual",
                r.Id, new { state = fromState }, new { state = r.State, refundId = refund.Id }, "manual_path", ct);

            return Results.Ok(new
            {
                id = refund.Id,
                state = refund.State,
                amountMinor = refund.AmountMinor,
                manual = true,
            });
        }

        // Online gateway path. Resolve gateway by ProviderId; fall back to first registered
        // gateway that supports the (market, method) combo.
        var gateways = services.GetServices<IPaymentGateway>().ToList();
        var gateway = gateways.FirstOrDefault(g =>
            string.Equals(g.ProviderId, refund.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (gateway is null)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 422, "refund.gateway_unavailable",
                $"No registered gateway matches provider '{refund.ProviderId}'.");
        }

        // Move to in_progress before calling out — if the call succeeds we land at completed,
        // if it fails we land at failed. The refund row is committed at in_progress to retain
        // the audit trail even if the process crashes mid-call.
        refund.State = RefundStateMachine.InProgress;
        refund.Attempts += 1;
        refund.UpdatedAt = nowUtc;
        db.StateTransitions.Add(new ReturnStateTransition
        {
            ReturnRequestId = r.Id,
            RefundId = refund.Id,
            Machine = ReturnStateTransition.MachineRefund,
            FromState = RefundStateMachine.Pending,
            ToState = RefundStateMachine.InProgress,
            ActorAccountId = actorId.Value,
            Trigger = "admin.issue_refund.gateway",
            OccurredAt = nowUtc,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Call gateway OUTSIDE the DB tx so a slow provider doesn't block other writes.
        RefundOutcome outcome;
        try
        {
            outcome = await gateway.RefundAsync(refund.CapturedTransactionId!, refund.AmountMinor,
                $"return:{r.ReturnNumber}", ct);
        }
        catch (Exception ex)
        {
            outcome = new RefundOutcome(false, "gateway.exception", ex.Message);
            logger.LogError(ex, "returns.issue_refund.gateway_threw refundId={RefundId}", refund.Id);
        }

        // Settle the outcome in a fresh transaction.
        await using var settleTx = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Refunds.FirstOrDefaultAsync(rf => rf.Id == refund.Id, ct);
        var lockedReturn = await db.ReturnRequests.FirstOrDefaultAsync(rr => rr.Id == r.Id, ct);
        if (locked is null || lockedReturn is null)
        {
            await settleTx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 500, "refund.state_lost", "Refund row vanished mid-flight.");
        }
        var settledNowUtc = DateTimeOffset.UtcNow;
        if (outcome.IsSuccess)
        {
            var fromReturn = lockedReturn.State;
            locked.State = RefundStateMachine.Completed;
            locked.GatewayRef = outcome.ErrorCode is null ? "ok" : null;
            locked.CompletedAt = settledNowUtc;
            locked.UpdatedAt = settledNowUtc;
            lockedReturn.State = ReturnStateMachine.Refunded;
            lockedReturn.UpdatedAt = settledNowUtc;
            db.StateTransitions.Add(new ReturnStateTransition
            {
                ReturnRequestId = lockedReturn.Id,
                RefundId = locked.Id,
                Machine = ReturnStateTransition.MachineRefund,
                FromState = RefundStateMachine.InProgress,
                ToState = RefundStateMachine.Completed,
                ActorAccountId = actorId.Value,
                Trigger = "admin.issue_refund.gateway_success",
                OccurredAt = settledNowUtc,
            });
            db.StateTransitions.Add(AdminMutation.NewReturnTransition(
                lockedReturn.Id, fromReturn, lockedReturn.State, actorId.Value, "admin.issue_refund",
                $"refundId={locked.Id}",
                new { refundId = locked.Id, amountMinor = locked.AmountMinor }, settledNowUtc));
            db.Outbox.Add(AdminMutation.NewOutbox("refund.completed", lockedReturn.Id, new
            {
                returnRequestId = lockedReturn.Id,
                returnNumber = lockedReturn.ReturnNumber,
                orderId = lockedReturn.OrderId,
                refundId = locked.Id,
                amountMinor = locked.AmountMinor,
                currency = locked.Currency,
                // Deep-review pass 1: explicit camelCase so the dispatcher's case-sensitive
                // `GetProperty("returnLineId")` matches; `new { l.ReturnLineId }` shorthand
                // would emit PascalCase and silently feed empty deltas downstream.
                lines = locked.Lines.Select(l => new { returnLineId = l.ReturnLineId, qty = l.Qty }),
            }, settledNowUtc));
        }
        else
        {
            locked.State = RefundStateMachine.Failed;
            locked.FailureReason = outcome.ErrorMessage ?? outcome.ErrorCode ?? "gateway_failure";
            locked.NextRetryAt = settledNowUtc.AddMinutes(5);
            locked.UpdatedAt = settledNowUtc;
            var fromReturn = lockedReturn.State;
            lockedReturn.State = ReturnStateMachine.RefundFailed;
            lockedReturn.UpdatedAt = settledNowUtc;
            db.StateTransitions.Add(new ReturnStateTransition
            {
                ReturnRequestId = lockedReturn.Id,
                RefundId = locked.Id,
                Machine = ReturnStateTransition.MachineRefund,
                FromState = RefundStateMachine.InProgress,
                ToState = RefundStateMachine.Failed,
                ActorAccountId = actorId.Value,
                Trigger = "admin.issue_refund.gateway_failure",
                Reason = locked.FailureReason,
                OccurredAt = settledNowUtc,
            });
            db.StateTransitions.Add(AdminMutation.NewReturnTransition(
                lockedReturn.Id, fromReturn, lockedReturn.State, actorId.Value, "admin.issue_refund.failed",
                $"refundId={locked.Id}",
                new { refundId = locked.Id, error = locked.FailureReason }, settledNowUtc));
            db.Outbox.Add(AdminMutation.NewOutbox("refund.failed", lockedReturn.Id, new
            {
                returnRequestId = lockedReturn.Id,
                refundId = locked.Id,
                amountMinor = locked.AmountMinor,
                error = locked.FailureReason,
            }, settledNowUtc));
        }
        await db.SaveChangesAsync(ct);
        await settleTx.CommitAsync(ct);

        await AdminMutation.PublishAuditAsync(auditPublisher, actorId.Value,
            outcome.IsSuccess ? "returns.issue_refund.completed" : "returns.issue_refund.failed",
            r.Id, new { refundId = refund.Id }, new
            {
                refundId = locked.Id,
                state = locked.State,
                amountMinor = locked.AmountMinor,
                error = locked.FailureReason,
            }, outcome.ErrorCode, ct);

        if (!outcome.IsSuccess)
        {
            return ReturnsResponseFactory.Problem(context, 502, "refund.gateway_failure",
                "Refund gateway error.",
                outcome.ErrorMessage,
                new Dictionary<string, object?>
                {
                    ["refundId"] = refund.Id,
                    ["retryAfter"] = locked.NextRetryAt,
                });
        }

        return Results.Ok(new
        {
            id = refund.Id,
            state = locked.State,
            amountMinor = locked.AmountMinor,
            currency = locked.Currency,
            gatewayRef = locked.GatewayRef,
        });
    }
}
