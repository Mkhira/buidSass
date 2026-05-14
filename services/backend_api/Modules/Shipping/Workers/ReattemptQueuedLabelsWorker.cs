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
/// T042 / AC-20 — periodically re-attempts shipments in
/// <c>pending_label_provider_failure</c> against the CURRENT routing
/// (so post-failover the next loop picks up the swapped primary
/// without operator intervention). Attempts are bounded by the same
/// 3-attempt budget enforced upstream; this worker is the gentle
/// nudge that keeps the queue from stalling between failover and
/// the next inbound order.
/// </summary>
public sealed class ReattemptQueuedLabelsWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ReattemptQueuedLabelsWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

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
                logger.LogError(ex, "ReattemptQueuedLabelsWorker iteration failed");
            }
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) { }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var providers = scope.ServiceProvider.GetRequiredService<ProviderRegistry>();
        var shipments = scope.ServiceProvider.GetRequiredService<ShipmentService>();

        var stuck = await db.Shipments
            .Where(s => s.State == ShipmentStates.PendingLabelProviderFailure && s.DeletedAt == null)
            .OrderBy(s => s.UpdatedAt)
            .Take(10)
            .ToListAsync(ct);

        foreach (var shipment in stuck)
        {
            // Resolve the CURRENT primary for (market, method) — so post-
            // failover this worker rolls onto the new primary automatically.
            var methodId = await db.ShippingMethodVersions
                .Where(v => v.Id == shipment.MethodVersionId)
                .Select(v => (Guid?)v.MethodId)
                .FirstOrDefaultAsync(ct);
            if (methodId is null) continue;

            var routing = await db.ProviderRoutings
                .FirstOrDefaultAsync(r => r.MarketCode == shipment.MarketCode && r.MethodId == methodId, ct);
            if (routing is null) continue;

            if (!providers.TryResolve(routing.PrimaryProviderId, out var provider) || provider is null)
            {
                logger.LogWarning("ReattemptQueuedLabels: provider {Id} not registered.", routing.PrimaryProviderId);
                continue;
            }

            // Reset attempts counter on routing change — the new provider gets
            // its own 3-attempt window.
            if (!string.Equals(shipment.ProviderId, routing.PrimaryProviderId, StringComparison.Ordinal))
            {
                shipment.ProviderId = routing.PrimaryProviderId;
                shipment.Attempts = 0;
                await db.SaveChangesAsync(ct);
            }

            var dispatch = new CreateShipmentDispatch(
                ShipmentId: shipment.Id,
                MarketCode: shipment.MarketCode,
                MethodKey: methodId.Value.ToString(),
                RecipientNameRedacted: "REDACTED",
                RecipientPhoneMaskedLast4: "****",
                ShipTo: new AddressMinimized("", null, "", null, shipment.MarketCode),
                WeightKg: 0m,
                CurrencyCode: ShippingConstants.Currencies.For(shipment.MarketCode),
                DeclaredValueAmount: 0m);

            var result = await provider.CreateShipmentAsync(dispatch, ct);
            if (result.Success && !string.IsNullOrEmpty(result.ProviderTrackingId))
            {
                shipment.ProviderTrackingId = result.ProviderTrackingId;
                shipment.LabelPdfBlobUrl = result.LabelPdfBlobUrl;
                shipment.EtaMin = result.EtaMin;
                shipment.EtaMax = result.EtaMax;
                await shipments.TransitionAsync(
                    shipment, ShipmentStates.LabelPurchased,
                    actorId: SystemActor.WorkerSystemActorId,
                    actorRole: "system",
                    reason: "reattempt_after_routing_change", ct);
            }
        }
    }
}
