using System.Text.Json;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Orders.Workers;

/// <summary>
/// G1 — daily tick that flips active quotations to <c>expired</c> when ValidUntil has passed.
/// Mirrors the catalog/scheduled-publishes worker pattern: short cron-like loop plus idempotent
/// transition.
/// </summary>
public sealed class QuotationExpiryWorker(
    IServiceProvider services,
    ILogger<QuotationExpiryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("orders.quotation_expiry.started interval={Interval}m", TickInterval.TotalMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expired = await ExpireBatchAsync(stoppingToken);
                if (expired > 0)
                {
                    logger.LogInformation("orders.quotation_expiry.expired count={Count}", expired);
                }
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "orders.quotation_expiry.error");
                await Task.Delay(TickInterval, stoppingToken);
            }
        }
    }

    private async Task<int> ExpireBatchAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;

        var pending = await db.Quotations
            .Where(q => (q.Status == Quotation.StatusActive || q.Status == Quotation.StatusDraft)
                && q.ValidUntil < nowUtc)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (pending.Count == 0) return 0;

        foreach (var q in pending)
        {
            q.Status = Quotation.StatusExpired;
            q.UpdatedAt = nowUtc;
            db.Outbox.Add(new OrdersOutboxEntry
            {
                EventType = "quote.expired",
                AggregateId = q.Id,
                PayloadJson = JsonSerializer.Serialize(new { quotationId = q.Id, expiredBy = "system" }),
                CommittedAt = nowUtc,
            });
        }
        await db.SaveChangesAsync(ct);
        return pending.Count;
    }
}
