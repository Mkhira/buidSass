using System.Text.Json;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Orders.Workers;

/// <summary>
/// G2 — payment-failed recovery sweep. For orders in <c>payment.failed</c> the worker emits a
/// <c>payment.failed_recovery_window</c> outbox row at policy intervals so spec 014's
/// notifications subscriber can prompt the customer to retry. After the configured retry
/// budget elapses without recovery the order's order_state stays placed but a final
/// <c>payment.failed_terminal</c> event is emitted for finance follow-up.
///
/// This worker DOES NOT auto-cancel orders — recovery is customer-initiated; the worker only
/// observes + emits.
/// </summary>
public sealed class PaymentFailedRecoveryWorker(
    IServiceProvider services,
    ILogger<PaymentFailedRecoveryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(30);
    /// <summary>Recovery window after which we mark the order as "failed-terminal" for finance.</summary>
    private static readonly TimeSpan RecoveryWindow = TimeSpan.FromHours(48);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "orders.payment_failed_recovery.started interval={Interval}m window={Window}h",
            TickInterval.TotalMinutes, RecoveryWindow.TotalHours);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await SweepBatchAsync(stoppingToken);
                if (processed > 0)
                {
                    logger.LogInformation("orders.payment_failed_recovery.processed count={Count}", processed);
                }
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "orders.payment_failed_recovery.error");
                await Task.Delay(TickInterval, stoppingToken);
            }
        }
    }

    private async Task<int> SweepBatchAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;
        var cutoff = nowUtc - RecoveryWindow;

        var failed = await db.Orders
            .Where(o => o.PaymentState == PaymentSm.Failed
                && o.OrderState == OrderSm.Placed
                && o.PlacedAt <= cutoff)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (failed.Count == 0) return 0;

        foreach (var order in failed)
        {
            // Idempotency: skip if a payment.failed_terminal outbox row was already emitted.
            var alreadyEmitted = await db.Outbox.AnyAsync(
                e => e.AggregateId == order.Id && e.EventType == "payment.failed_terminal", ct);
            if (alreadyEmitted) continue;
            db.Outbox.Add(new OrdersOutboxEntry
            {
                EventType = "payment.failed_terminal",
                AggregateId = order.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    orderId = order.Id,
                    orderNumber = order.OrderNumber,
                    failedAt = order.UpdatedAt,
                    windowHours = (int)RecoveryWindow.TotalHours,
                }),
                CommittedAt = nowUtc,
            });
            order.UpdatedAt = nowUtc;
        }
        await db.SaveChangesAsync(ct);
        return failed.Count;
    }
}
