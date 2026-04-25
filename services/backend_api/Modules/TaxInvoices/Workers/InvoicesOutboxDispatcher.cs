using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.TaxInvoices.Workers;

/// <summary>
/// G — invoices_outbox dispatcher. Subscribers (spec 019 notifications, finance analytics)
/// will consume <c>invoice.issued</c>, <c>invoice.regenerated</c>, <c>credit_note.issued</c>
/// in follow-up specs; for Phase 1B this dispatcher logs the events so the trail is
/// observable in stdout and marks rows dispatched.
/// </summary>
public sealed class InvoicesOutboxDispatcher(
    IServiceProvider services,
    ILogger<InvoicesOutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("invoices.outbox_dispatcher.started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchBatchAsync(stoppingToken);
                if (dispatched == 0) await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "invoices.outbox_dispatcher.cycle_failed");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        var pending = await db.Outbox
            .Where(e => e.DispatchedAt == null)
            .OrderBy(e => e.CommittedAt)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (pending.Count == 0) return 0;
        var nowUtc = DateTimeOffset.UtcNow;
        foreach (var entry in pending)
        {
            logger.LogInformation(
                "invoices.outbox.dispatched id={Id} type={Type} aggregate={AggregateId}",
                entry.Id, entry.EventType, entry.AggregateId);
            entry.DispatchedAt = nowUtc;
        }
        await db.SaveChangesAsync(ct);
        return pending.Count;
    }
}
