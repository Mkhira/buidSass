using System.Text.Json;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Internal.IssueOnCapture;
using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.TaxInvoices.Workers;

/// <summary>
/// Cross-module subscription on spec 011's <c>orders.orders_outbox</c>: every
/// <c>payment.captured</c> emission triggers <see cref="IssueOnCaptureHandler"/>. We track
/// our own watermark in <see cref="SubscriptionCheckpoint"/> rather than mutating spec 011's
/// <c>DispatchedAt</c> column (which is owned by spec 011's own dispatcher). This is the
/// outbox-as-message-bus pattern — at-least-once + idempotent consumer.
/// </summary>
public sealed class PaymentCapturedSubscriber(
    IServiceProvider services,
    ILogger<PaymentCapturedSubscriber> logger) : BackgroundService
{
    private const string SourceModule = "orders";
    private const string EventType = "payment.captured";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("invoices.payment_captured_subscriber.started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await PollOnceAsync(stoppingToken);
                if (processed == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "invoices.payment_captured_subscriber.cycle_failed");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task<int> PollOnceAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var invoicesDb = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var handler = scope.ServiceProvider.GetRequiredService<IssueOnCaptureHandler>();

        var checkpoint = await invoicesDb.SubscriptionCheckpoints
            .FirstOrDefaultAsync(c => c.SourceModule == SourceModule && c.EventType == EventType, ct);
        long watermark = checkpoint?.LastObservedOutboxId ?? 0;

        var rows = await ordersDb.Outbox.AsNoTracking()
            .Where(e => e.EventType == EventType && e.Id > watermark)
            .OrderBy(e => e.Id)
            .Take(BatchSize)
            .Select(e => new { e.Id, e.AggregateId, e.PayloadJson })
            .ToListAsync(ct);
        if (rows.Count == 0) return 0;

        var processedCount = 0;
        foreach (var row in rows)
        {
            // The payload includes orderId; fall back to AggregateId (order id) if missing.
            var orderId = ExtractOrderId(row.PayloadJson) ?? row.AggregateId;
            try
            {
                var result = await handler.IssueAsync(orderId, ct);
                if (!result.IsSuccess)
                {
                    // Skippable reason codes represent legitimate state-mismatch events that
                    // should NOT be retried (the order will never advance into an issuable state
                    // for this captured event). Anything else — DB errors, template missing,
                    // duplicate-key races — is transient: HALT so the next poll retries from
                    // this row's id rather than skipping past it.
                    var skippable =
                        result.ErrorCode is "invoice.payment_not_captured"
                            or "invoice.no_lines"
                            or "invoice.order_not_found";
                    if (!skippable)
                    {
                        logger.LogError(
                            "invoices.subscriber.issue_failed_halt orderId={OrderId} outboxId={OutboxId} reason={Reason}",
                            orderId, row.Id, result.ErrorCode);
                        // CR-style fix: leave watermark at the last successful row so retry
                        // re-attempts THIS row on the next poll. Without this halt, transient
                        // failures silently dropped invoice issuances.
                        break;
                    }
                    logger.LogInformation(
                        "invoices.subscriber.issue_skipped orderId={OrderId} reason={Reason}",
                        orderId, result.ErrorCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "invoices.subscriber.issue_threw orderId={OrderId}", orderId);
                // Halt at this row — leave watermark untouched so the next poll retries.
                break;
            }
            watermark = row.Id;
            processedCount++;
        }

        if (checkpoint is null)
        {
            invoicesDb.SubscriptionCheckpoints.Add(new SubscriptionCheckpoint
            {
                SourceModule = SourceModule,
                EventType = EventType,
                LastObservedOutboxId = watermark,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            checkpoint.LastObservedOutboxId = watermark;
            checkpoint.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await invoicesDb.SaveChangesAsync(ct);
        return processedCount;
    }

    private static Guid? ExtractOrderId(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("orderId", out var prop)
                && prop.ValueKind == JsonValueKind.String
                && Guid.TryParse(prop.GetString(), out var id))
            {
                return id;
            }
        }
        catch (JsonException) { }
        return null;
    }
}
