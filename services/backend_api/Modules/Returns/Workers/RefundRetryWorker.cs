using System.Text.Json;
using BackendApi.Modules.Checkout.Primitives.Payment;
using BackendApi.Modules.Returns.Admin.Common;
using BackendApi.Modules.Returns.Entities;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Returns.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Returns.Workers;

/// <summary>
/// FR-021 / Phase I. Retries refunds in state <c>failed</c> with <c>NextRetryAt &lt;= now()</c>.
/// Exponential backoff with jitter; admin's <c>POST /v1/admin/refunds/{id}/retry</c> sets
/// <c>NextRetryAt</c> to now so manual triggers are immediate.
/// </summary>
public sealed class RefundRetryWorker(
    IServiceProvider services,
    ILogger<RefundRetryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private const int BatchSize = 20;
    private const int MaxAttemptsBeforePark = 8;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("returns.refund_retry_worker.started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await TickAsync(stoppingToken);
                if (processed == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "returns.refund_retry_worker.error");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task<int> TickAsync(CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReturnsDbContext>();
        var gateways = scope.ServiceProvider.GetServices<IPaymentGateway>().ToList();

        var due = await db.Refunds
            .Include(rf => rf.Lines)
            .Where(rf => rf.State == RefundStateMachine.Failed
                && rf.NextRetryAt != null && rf.NextRetryAt <= nowUtc
                && rf.Attempts < MaxAttemptsBeforePark)
            .OrderBy(rf => rf.NextRetryAt)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (due.Count == 0) return 0;

        foreach (var refund in due)
        {
            var gateway = gateways.FirstOrDefault(g =>
                string.Equals(g.ProviderId, refund.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (gateway is null
                || string.IsNullOrWhiteSpace(refund.CapturedTransactionId))
            {
                logger.LogWarning("returns.refund_retry.no_gateway refundId={RefundId} provider={Provider}",
                    refund.Id, refund.ProviderId);
                refund.NextRetryAt = nowUtc.Add(BackoffFor(refund.Attempts + 1));
                refund.UpdatedAt = nowUtc;
                continue;
            }

            // Move to in_progress before the call.
            var fromState = refund.State;
            refund.State = RefundStateMachine.InProgress;
            refund.Attempts += 1;
            refund.UpdatedAt = nowUtc;
            db.StateTransitions.Add(new ReturnStateTransition
            {
                ReturnRequestId = refund.ReturnRequestId,
                RefundId = refund.Id,
                Machine = ReturnStateTransition.MachineRefund,
                FromState = fromState,
                ToState = RefundStateMachine.InProgress,
                Trigger = "worker.retry",
                OccurredAt = nowUtc,
            });
            await db.SaveChangesAsync(ct);

            RefundOutcome outcome;
            try
            {
                var r = await db.ReturnRequests.AsNoTracking()
                    .Where(rr => rr.Id == refund.ReturnRequestId)
                    .Select(rr => rr.ReturnNumber).FirstOrDefaultAsync(ct);
                outcome = await gateway.RefundAsync(refund.CapturedTransactionId,
                    refund.AmountMinor, $"return:{r}", ct);
            }
            catch (Exception ex)
            {
                outcome = new RefundOutcome(false, "gateway.exception", ex.Message);
                logger.LogError(ex, "returns.refund_retry.gateway_threw refundId={RefundId}", refund.Id);
            }

            var settledNowUtc = DateTimeOffset.UtcNow;
            if (outcome.IsSuccess)
            {
                refund.State = RefundStateMachine.Completed;
                refund.GatewayRef = "ok";
                refund.CompletedAt = settledNowUtc;
                refund.NextRetryAt = null;
                refund.UpdatedAt = settledNowUtc;
                db.StateTransitions.Add(new ReturnStateTransition
                {
                    ReturnRequestId = refund.ReturnRequestId,
                    RefundId = refund.Id,
                    Machine = ReturnStateTransition.MachineRefund,
                    FromState = RefundStateMachine.InProgress,
                    ToState = RefundStateMachine.Completed,
                    Trigger = "worker.retry.success",
                    OccurredAt = settledNowUtc,
                });

                var rr = await db.ReturnRequests.FirstOrDefaultAsync(rr2 => rr2.Id == refund.ReturnRequestId, ct);
                if (rr is not null && ReturnStateMachine.IsValidTransition(rr.State, ReturnStateMachine.Refunded))
                {
                    var fromReturn = rr.State;
                    rr.State = ReturnStateMachine.Refunded;
                    rr.UpdatedAt = settledNowUtc;
                    db.StateTransitions.Add(AdminMutation.NewReturnTransition(
                        rr.Id, fromReturn, rr.State, Guid.Empty, "worker.refund_retry",
                        $"refundId={refund.Id}",
                        new { refundId = refund.Id }, settledNowUtc));
                    db.Outbox.Add(AdminMutation.NewOutbox("refund.completed", rr.Id, new
                    {
                        returnRequestId = rr.Id,
                        returnNumber = rr.ReturnNumber,
                        orderId = rr.OrderId,
                        refundId = refund.Id,
                        amountMinor = refund.AmountMinor,
                        currency = refund.Currency,
                        lines = refund.Lines.Select(l => new { returnLineId = l.ReturnLineId, qty = l.Qty }),
                    }, settledNowUtc));
                }
            }
            else
            {
                refund.State = RefundStateMachine.Failed;
                refund.FailureReason = outcome.ErrorMessage ?? outcome.ErrorCode ?? "gateway_failure";
                refund.NextRetryAt = settledNowUtc.Add(BackoffFor(refund.Attempts));
                refund.UpdatedAt = settledNowUtc;
                db.StateTransitions.Add(new ReturnStateTransition
                {
                    ReturnRequestId = refund.ReturnRequestId,
                    RefundId = refund.Id,
                    Machine = ReturnStateTransition.MachineRefund,
                    FromState = RefundStateMachine.InProgress,
                    ToState = RefundStateMachine.Failed,
                    Trigger = "worker.retry.failure",
                    Reason = refund.FailureReason,
                    OccurredAt = settledNowUtc,
                });
            }
            await db.SaveChangesAsync(ct);
        }
        return due.Count;
    }

    private static TimeSpan BackoffFor(int attempts)
    {
        // 30s × 2^min(attempts,8) capped at 1h.
        var seconds = Math.Min(3600, 30 * Math.Pow(2, Math.Min(attempts, 8)));
        return TimeSpan.FromSeconds(seconds);
    }
}
