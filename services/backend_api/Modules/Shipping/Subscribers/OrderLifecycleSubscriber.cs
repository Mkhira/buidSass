using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Shipping.Domain;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using BackendApi.Modules.Shipping.Providers;
using BackendApi.Modules.Shipping.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Shipping.Subscribers;

/// <summary>
/// T022 + T023 + T024 — single subscriber implementation handling
/// <see cref="OrderConfirmedEvent"/>, <see cref="OrderCancelledEvent"/>,
/// and <see cref="OrderRefundInitiatedEvent"/>. Per project memory we wire
/// cross-module hooks via <see cref="IOrderLifecycleSubscriber"/> in
/// <c>Modules/Shared/</c> to avoid module-to-module cycles.
/// </summary>
public sealed class OrderLifecycleSubscriber(
    ShippingDbContext db,
    ShipmentService shipments,
    ProviderRegistry providers,
    IAuditEventPublisher audit,
    TimeProvider clock,
    ILogger<OrderLifecycleSubscriber> logger) : IOrderLifecycleSubscriber
{
    public async Task OnOrderConfirmedAsync(OrderConfirmedEvent @event, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (!@event.ShippingMethodVersionId.HasValue)
        {
            logger.LogWarning("OrderConfirmed for {OrderId} carries no shipping method version; skipping.", @event.OrderId);
            return;
        }

        // BR-4 — one shipment per order; idempotent on order_id (the
        // partial unique index also guards this at the DB layer).
        var existing = await db.Shipments
            .FirstOrDefaultAsync(s => s.OrderId == @event.OrderId && s.DeletedAt == null, ct);
        if (existing is not null)
        {
            logger.LogInformation("OrderConfirmed for {OrderId}: shipment already exists ({ShipmentId}).",
                @event.OrderId, existing.Id);
            return;
        }

        // Resolve the provider via routing config (primary at v1; failover is operator-driven).
        var methodId = await db.ShippingMethodVersions
            .Where(v => v.Id == @event.ShippingMethodVersionId)
            .Select(v => (Guid?)v.MethodId)
            .FirstOrDefaultAsync(ct);
        if (methodId is null)
        {
            logger.LogError("OrderConfirmed for {OrderId}: method version {VersionId} not found.",
                @event.OrderId, @event.ShippingMethodVersionId);
            return;
        }

        var routing = await db.ProviderRoutings
            .FirstOrDefaultAsync(r => r.MarketCode == @event.MarketCode && r.MethodId == methodId.Value, ct);
        if (routing is null)
        {
            logger.LogError("OrderConfirmed for {OrderId}: no provider routing for ({Market}, {MethodId}).",
                @event.OrderId, @event.MarketCode, methodId);
            return;
        }

        var providerId = routing.PrimaryProviderId;
        if (!providers.TryResolve(providerId, out var provider) || provider is null)
        {
            logger.LogError("OrderConfirmed for {OrderId}: provider {ProviderId} not registered.",
                @event.OrderId, providerId);
            return;
        }

        var shipmentId = Guid.NewGuid();
        var nowUtc = clock.GetUtcNow();

        // BR-15 — minimum-sufficient redacted address surface.
        var redactedShipTo = new
        {
            recipient_name = RedactName(@event.ShipTo.RecipientName),
            recipient_phone_last4 = MaskPhone(@event.ShipTo.RecipientPhone),
            city = @event.ShipTo.City,
            postal_code = @event.ShipTo.PostalCode,
            line1 = @event.ShipTo.Line1,
            line2 = @event.ShipTo.Line2,
            country = @event.ShipTo.CountryCode,
        };
        var shipment = new Shipment
        {
            Id = shipmentId,
            OrderId = @event.OrderId,
            MarketCode = @event.MarketCode,
            MethodVersionId = @event.ShippingMethodVersionId.Value,
            ProviderId = providerId,
            State = ShipmentStates.Pending,
            ShipToAddressRedactedJson = JsonSerializer.Serialize(redactedShipTo),
            Attempts = 0,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync(ct);

        // Build the PII-minimized provider dispatch (BR-15).
        var dispatch = new CreateShipmentDispatch(
            ShipmentId: shipmentId,
            MarketCode: @event.MarketCode,
            MethodKey: methodId.Value.ToString(),
            RecipientNameRedacted: RedactName(@event.ShipTo.RecipientName),
            RecipientPhoneMaskedLast4: MaskPhone(@event.ShipTo.RecipientPhone),
            ShipTo: new AddressMinimized(
                @event.ShipTo.City,
                @event.ShipTo.PostalCode,
                @event.ShipTo.Line1,
                @event.ShipTo.Line2,
                @event.ShipTo.CountryCode),
            WeightKg: @event.WeightKg,
            CurrencyCode: @event.CurrencyCode,
            DeclaredValueAmount: @event.CurrencyAmount);

        var result = await provider.CreateShipmentAsync(dispatch, ct);
        if (result.Success && !string.IsNullOrEmpty(result.ProviderTrackingId))
        {
            shipment.ProviderTrackingId = result.ProviderTrackingId;
            shipment.LabelPdfBlobUrl = result.LabelPdfBlobUrl;
            shipment.EtaMin = result.EtaMin;
            shipment.EtaMax = result.EtaMax;
            await shipments.TransitionAsync(
                shipment: shipment,
                newState: ShipmentStates.LabelPurchased,
                actorId: SystemActor.WorkerSystemActorId,
                actorRole: "system",
                reason: "order_confirmed",
                ct: ct);
        }
        else
        {
            shipment.FailedReason = result.FailureReason;
            await shipments.TransitionAsync(
                shipment: shipment,
                newState: ShipmentStates.FailedToCreateLabel,
                actorId: SystemActor.WorkerSystemActorId,
                actorRole: "system",
                reason: $"provider_failure:{result.FailureCode ?? "unknown"}",
                ct: ct);
            await audit.PublishAsync(new AuditEvent(
                ActorId: SystemActor.WorkerSystemActorId,
                ActorRole: "system",
                Action: ShippingConstants.AuditActions.LabelCreationFailed,
                EntityType: ShippingConstants.EntityTypes.Shipment,
                EntityId: shipment.Id,
                BeforeState: null,
                AfterState: new { provider_id = providerId, failure_code = result.FailureCode },
                Reason: result.FailureReason), ct);
        }
    }

    public async Task OnOrderCancelledAsync(OrderCancelledEvent @event, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var shipment = await db.Shipments
            .FirstOrDefaultAsync(s => s.OrderId == @event.OrderId && s.DeletedAt == null, ct);
        if (shipment is null) return;

        // Only cascade if the current state allows label_voided (active path).
        var canVoid = !ShipmentStates.IsTerminal(shipment.State);
        if (!canVoid) return;

        // Call the provider's void-label API if a tracking id exists; ignore the
        // result (provider-side void failures don't block our state change).
        if (!string.IsNullOrEmpty(shipment.ProviderTrackingId)
            && providers.TryResolve(shipment.ProviderId, out var provider)
            && provider is not null)
        {
            await provider.VoidLabelAsync(shipment.ProviderTrackingId, ct);
        }

        await shipments.TransitionAsync(
            shipment: shipment,
            newState: ShipmentStates.LabelVoided,
            actorId: SystemActor.WorkerSystemActorId,
            actorRole: "system",
            reason: "order_cancelled",
            ct: ct);

        await audit.PublishAsync(new AuditEvent(
            ActorId: SystemActor.WorkerSystemActorId,
            ActorRole: "system",
            Action: ShippingConstants.AuditActions.LabelVoided,
            EntityType: ShippingConstants.EntityTypes.Shipment,
            EntityId: shipment.Id,
            BeforeState: null,
            AfterState: new { reason = @event.Reason },
            Reason: @event.Reason), ct);
    }

    public Task OnOrderRefundInitiatedAsync(OrderRefundInitiatedEvent @event, CancellationToken ct)
    {
        // V1 — return-shipment creation is operator-driven via the admin UI's
        // "Create re-delivery" / "Initiate return" actions. The subscriber
        // exists to keep the cross-module contract whole; richer automation
        // (auto-creating return shipments for digital + standard returns)
        // lands in Phase 1.5 alongside the returns-workflow polish.
        logger.LogInformation(
            "OrderRefundInitiated for {OrderId} (requires_return={RequiresReturn}); no automated action at v1.",
            @event.OrderId, @event.RequiresReturnShipment);
        return Task.CompletedTask;
    }

    private static string RedactName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "REDACTED";
        var trimmed = name.Trim();
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return $"{parts[0][0]}.";
        return $"{parts[0]} {parts[^1][0]}.";
    }

    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "****";
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length <= 4) return digits;
        return $"****{digits[^4..]}";
    }
}
