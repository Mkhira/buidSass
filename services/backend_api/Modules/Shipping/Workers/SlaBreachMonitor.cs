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
/// T044 — alerts when a shipment is older than the per-market SLA × 2
/// (per Edge Case "Carrier strike or extended delivery delay"). Emits
/// <c>shipping.sla_breach</c> audit + <see cref="ShipmentSlaBreached"/>
/// MediatR notification. Idempotent: the audit row's correlation_id
/// keeps duplicate emissions on subsequent polls easily filterable.
/// </summary>
public sealed class SlaBreachMonitor(
    IServiceScopeFactory scopeFactory,
    ILogger<SlaBreachMonitor> logger,
    TimeProvider clock) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "SlaBreachMonitor iteration failed");
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
        var schemas = await db.MarketSchemas.ToDictionaryAsync(s => s.MarketCode, ct);
        if (schemas.Count == 0) return;

        var active = await db.Shipments
            .Where(s => s.DeletedAt == null
                     && (s.State == ShipmentStates.InTransit
                       || s.State == ShipmentStates.OutForDelivery
                       || s.State == ShipmentStates.HandedToCarrier
                       || s.State == ShipmentStates.DeliveryAttempted))
            .ToListAsync(ct);

        foreach (var shipment in active)
        {
            if (!schemas.TryGetValue(shipment.MarketCode, out var schema)) continue;
            var ageHours = (int)Math.Round((nowUtc - shipment.CreatedAt).TotalHours);
            var threshold = schema.SlaBreachThresholdHours * 2;
            if (ageHours < threshold) continue;

            await audit.PublishAsync(new AuditEvent(
                ActorId: Services.SystemActor.WorkerSystemActorId,
                ActorRole: "system",
                Action: ShippingConstants.AuditActions.SlaBreach,
                EntityType: ShippingConstants.EntityTypes.Shipment,
                EntityId: shipment.Id,
                BeforeState: null,
                AfterState: new { age_hours = ageHours, threshold_hours = threshold },
                Reason: "age_exceeds_sla_x2"), ct);

            await events.Publish(new ShipmentSlaBreached(
                shipment.Id, shipment.OrderId, shipment.MarketCode, ageHours, nowUtc), ct);
        }
    }
}
