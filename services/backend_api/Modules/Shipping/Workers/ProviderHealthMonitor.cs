using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shipping.Domain.Events;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Shipping.Workers;

/// <summary>
/// T043 — 5-minute sliding-window provider health monitor. Computes the
/// label-creation failure rate per <c>(market, primary_provider)</c> and
/// fires <c>provider.degraded</c> when the rate exceeds the per-routing
/// threshold. If <c>auto_failover_enabled = true</c> for that row (off
/// by default — BR-11), it ALSO performs the swap. Otherwise the event
/// is purely informational and an operator must act.
/// </summary>
public sealed class ProviderHealthMonitor(
    IServiceScopeFactory scopeFactory,
    ILogger<ProviderHealthMonitor> logger,
    TimeProvider clock) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SlidingWindow = TimeSpan.FromMinutes(5);
    private const int MinSampleSize = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ProviderHealthMonitor iteration failed");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
        var events = scope.ServiceProvider.GetRequiredService<IPublisher>();

        var nowUtc = clock.GetUtcNow();
        var windowStart = nowUtc - SlidingWindow;

        var routings = await db.ProviderRoutings.ToListAsync(ct);
        foreach (var routing in routings)
        {
            // Recent shipments per (market, primary_provider) inside the window.
            var recent = await db.Shipments
                .Where(s => s.MarketCode == routing.MarketCode
                         && s.ProviderId == routing.PrimaryProviderId
                         && s.CreatedAt >= windowStart
                         && s.DeletedAt == null)
                .Select(s => s.State)
                .ToListAsync(ct);
            if (recent.Count < MinSampleSize) continue;

            int failures = recent.Count(s =>
                s == ShipmentStates.FailedToCreateLabel ||
                s == ShipmentStates.PendingLabelProviderFailure ||
                s == ShipmentStates.DeadLetterLabel);
            int pct = (int)Math.Round(100.0 * failures / recent.Count);
            if (pct < routing.FailoverThresholdPct) continue;

            await audit.PublishAsync(new AuditEvent(
                ActorId: Services.SystemActor.WorkerSystemActorId,
                ActorRole: "system",
                Action: ShippingConstants.AuditActions.ProviderDegraded,
                EntityType: ShippingConstants.EntityTypes.ProviderRouting,
                EntityId: routing.MethodId,
                BeforeState: null,
                AfterState: new
                {
                    routing.MarketCode,
                    routing.PrimaryProviderId,
                    failure_pct = pct,
                    sample_size = recent.Count,
                },
                Reason: $"failure_pct={pct} >= threshold_pct={routing.FailoverThresholdPct}"), ct);

            await events.Publish(new ProviderDegraded(
                routing.PrimaryProviderId, routing.MarketCode, pct, nowUtc), ct);

            // BR-11 — auto-failover only when explicitly enabled (default OFF).
            if (routing.AutoFailoverEnabled && !string.IsNullOrEmpty(routing.BackupProviderId))
            {
                var oldPrimary = routing.PrimaryProviderId;
                var oldBackup = routing.BackupProviderId!;
                // Compare-and-swap via ExecuteUpdateAsync — only flips when the
                // current primary still matches the value we read. Avoids
                // double-flipping if a second worker observed the same threshold
                // simultaneously (multi-instance safety). Returns 0 when another
                // actor (manual failover, prior auto-failover loop) already
                // swapped the routing; in that case we skip emit + audit.
                var swapped = await db.ProviderRoutings
                    .Where(r => r.MarketCode == routing.MarketCode
                             && r.MethodId == routing.MethodId
                             && r.PrimaryProviderId == oldPrimary
                             && r.BackupProviderId == oldBackup)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.PrimaryProviderId, oldBackup)
                        .SetProperty(r => r.BackupProviderId, oldPrimary)
                        .SetProperty(r => r.UpdatedAt, nowUtc), ct);
                if (swapped == 0) continue;

                await audit.PublishAsync(new AuditEvent(
                    ActorId: Services.SystemActor.WorkerSystemActorId,
                    ActorRole: "system",
                    Action: ShippingConstants.AuditActions.ProviderFailover,
                    EntityType: ShippingConstants.EntityTypes.ProviderRouting,
                    EntityId: routing.MethodId,
                    BeforeState: new { primary = oldPrimary, backup = oldBackup },
                    AfterState: new { primary = oldBackup, backup = oldPrimary },
                    Reason: "auto_failover_threshold_exceeded"), ct);
                await events.Publish(new ProviderFailoverPerformed(
                    routing.MarketCode, routing.MethodId, oldPrimary, oldBackup, nowUtc), ct);
            }
        }
    }
}
