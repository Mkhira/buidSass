using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shipping.Domain;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using BackendApi.Modules.Shipping.Providers;
using BackendApi.Modules.Shipping.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Shipping.Workers;

/// <summary>
/// T025 — periodically retries shipments stuck in <c>failed_to_create_label</c>
/// with the 3-attempt budget (1s, 3s, 9s) per BR-5. After the third
/// failure the shipment transitions to <c>pending_label_provider_failure</c>
/// and a dead-letter row is created (AC-8).
///
/// This runs as a <see cref="BackgroundService"/> (no Hangfire dependency
/// in this repo; the CMS / Support modules use the same pattern).
/// </summary>
public sealed class LabelDispatchWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<LabelDispatchWorker> logger,
    TimeProvider clock) : BackgroundService
{
    // Reserved for delay-window precision once we move from Task.Delay to a
    // clock-driven scheduler. Held for parity with the other workers' ctor.
    private readonly TimeProvider _clock = clock;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly int[] AttemptDelaysSeconds = { 1, 3, 9 };
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "LabelDispatchWorker iteration failed");
            }
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var providers = scope.ServiceProvider.GetRequiredService<ProviderRegistry>();
        var shipments = scope.ServiceProvider.GetRequiredService<ShipmentService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();

        var pending = await db.Shipments
            .Where(s => s.State == ShipmentStates.FailedToCreateLabel && s.DeletedAt == null)
            .OrderBy(s => s.UpdatedAt)
            .Take(20)
            .ToListAsync(ct);

        foreach (var shipment in pending)
        {
            if (shipment.LabelCreationAttempts >= MaxAttempts)
            {
                await EnterDeadLetterAsync(db, audit, shipment, ct);
                continue;
            }

            if (!providers.TryResolve(shipment.ProviderId, out var provider) || provider is null)
            {
                logger.LogWarning("LabelDispatchWorker: provider {Provider} not registered for shipment {Id}",
                    shipment.ProviderId, shipment.Id);
                continue;
            }

            var attemptNo = shipment.LabelCreationAttempts + 1;
            var delay = TimeSpan.FromSeconds(AttemptDelaysSeconds[Math.Min(attemptNo - 1, AttemptDelaysSeconds.Length - 1)]);
            await Task.Delay(delay, ct);

            shipment.LabelCreationAttempts = attemptNo;
            await db.SaveChangesAsync(ct);

            var dispatch = ShipmentDispatchFactory.FromShipment(shipment);
            var result = await provider.CreateShipmentAsync(dispatch, ct);
            if (result.Success && !string.IsNullOrEmpty(result.ProviderTrackingId))
            {
                shipment.ProviderTrackingId = result.ProviderTrackingId;
                shipment.LabelPdfBlobUrl = result.LabelPdfBlobUrl;
                shipment.EtaMin = result.EtaMin;
                shipment.EtaMax = result.EtaMax;
                await shipments.TransitionAsync(
                    shipment, ShipmentStates.LabelPurchased,
                    actorId: SystemActor.WorkerSystemActorId, actorRole: "system",
                    reason: $"retry_attempt_{attemptNo}", ct);
            }
            else if (attemptNo >= MaxAttempts)
            {
                await shipments.TransitionAsync(
                    shipment, ShipmentStates.PendingLabelProviderFailure,
                    actorId: SystemActor.WorkerSystemActorId, actorRole: "system",
                    reason: $"retry_exhausted:{result.FailureCode ?? "unknown"}", ct);
                await EnterDeadLetterAsync(db, audit, shipment, ct);
            }
        }
    }

    /// <summary>
    /// Idempotently moves the shipment into <c>dead_letter_labels</c>.
    /// Uses an upsert pattern so a second concurrent worker call (or a
    /// post-failover repeat) does not violate the PK. Audit event is
    /// only emitted on first-time entry to avoid duplicate alerts.
    /// </summary>
    private static async Task EnterDeadLetterAsync(
        ShippingDbContext db, IAuditEventPublisher audit,
        Shipment shipment, CancellationToken ct)
    {
        var existing = await db.DeadLetterLabels
            .FirstOrDefaultAsync(d => d.ShipmentId == shipment.Id, ct);
        if (existing is not null)
        {
            // Refresh the last-error fields; treat as already-alerted.
            existing.LastErrorMessageRedacted = shipment.FailedReason;
            existing.LastErrorCode = "retry_exhausted";
            await db.SaveChangesAsync(ct);
            return;
        }
        db.DeadLetterLabels.Add(new DeadLetterLabel
        {
            ShipmentId = shipment.Id,
            LastErrorMessageRedacted = shipment.FailedReason,
            LastErrorCode = "retry_exhausted",
            EnteredAt = DateTimeOffset.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Race lost — another worker inserted the row first. Treat as success.
            return;
        }
        await audit.PublishAsync(new AuditEvent(
            ActorId: SystemActor.WorkerSystemActorId,
            ActorRole: "system",
            Action: ShippingConstants.AuditActions.DeadLetter,
            EntityType: ShippingConstants.EntityTypes.Shipment,
            EntityId: shipment.Id,
            BeforeState: null,
            AfterState: new { state = shipment.State },
            Reason: "label_creation_retry_exhausted"), ct);
    }
}
