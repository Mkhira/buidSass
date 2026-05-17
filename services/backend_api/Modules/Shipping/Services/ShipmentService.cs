using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shipping.Domain;
using BackendApi.Modules.Shipping.Domain.Events;
using BackendApi.Modules.Shipping.Domain.StateMachines;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Services;

/// <summary>
/// Owns the Shipment write paths so state transitions, audit-event
/// emission, and MediatR domain-event publication stay co-located.
/// Every state-changing method emits one audit row + one domain event.
/// (BR-10, AC-21, AC-13.)
/// </summary>
public sealed class ShipmentService(
    ShippingDbContext db,
    IAuditEventPublisher audit,
    IPublisher events,
    TimeProvider clock)
{
    /// <summary>
    /// Applies a state advance through the state machine, persists the
    /// new state, writes an audit row, and publishes
    /// <see cref="ShipmentStatusChanged"/>. Throws if the transition
    /// is not allowed by <see cref="ShipmentStateMachine"/>.
    /// </summary>
    public async Task TransitionAsync(
        Shipment shipment,
        string newState,
        Guid actorId,
        string actorRole,
        string? reason,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        var fromState = shipment.State;
        if (string.Equals(fromState, newState, StringComparison.Ordinal))
        {
            return; // no-op
        }
        ShipmentStateMachine.EnsureTransition(fromState, newState);

        var nowUtc = clock.GetUtcNow();
        shipment.State = newState;
        shipment.UpdatedAt = nowUtc;

        // Stamp the per-state timestamp columns so reporting + the SLA
        // breach worker can scan without recomputing from events.
        switch (newState)
        {
            case ShipmentStates.LabelPurchased:
                shipment.LabelPurchasedAt = nowUtc;
                break;
            case ShipmentStates.HandedToCarrier:
                shipment.HandedAt = nowUtc;
                break;
            case ShipmentStates.InTransit:
                shipment.InTransitAt = nowUtc;
                break;
            case ShipmentStates.Delivered:
                shipment.DeliveredAt = nowUtc;
                break;
            case ShipmentStates.DeliveryAttempted:
                shipment.DeliveryAttempts += 1;
                break;
        }

        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: ShippingConstants.AuditActions.StatusChanged,
            EntityType: ShippingConstants.EntityTypes.Shipment,
            EntityId: shipment.Id,
            BeforeState: new { state = fromState },
            AfterState: new { state = newState },
            Reason: reason), ct);

        await events.Publish(new ShipmentStatusChanged(
            ShipmentId: shipment.Id,
            OrderId: shipment.OrderId,
            MarketCode: shipment.MarketCode,
            ProviderId: shipment.ProviderId,
            ProviderTrackingId: shipment.ProviderTrackingId,
            FromState: fromState,
            ToState: newState,
            OccurredAt: nowUtc), ct);

        if (newState == ShipmentStates.LabelPurchased && !string.IsNullOrEmpty(shipment.ProviderTrackingId))
        {
            await audit.PublishAsync(new AuditEvent(
                ActorId: actorId,
                ActorRole: actorRole,
                Action: ShippingConstants.AuditActions.LabelPurchased,
                EntityType: ShippingConstants.EntityTypes.Shipment,
                EntityId: shipment.Id,
                BeforeState: null,
                AfterState: new
                {
                    provider_id = shipment.ProviderId,
                    tracking_id = shipment.ProviderTrackingId,
                },
                Reason: null), ct);

            await events.Publish(new ShipmentLabelPurchased(
                ShipmentId: shipment.Id,
                OrderId: shipment.OrderId,
                MarketCode: shipment.MarketCode,
                ProviderId: shipment.ProviderId,
                ProviderTrackingId: shipment.ProviderTrackingId!,
                OccurredAt: nowUtc), ct);
        }
    }

    /// <summary>
    /// Records a webhook event into <c>shipment_events</c> and decides
    /// (per BR-12 precedence) whether to advance the shipment state.
    /// Idempotent on the <c>webhooks_received</c> PK.
    /// </summary>
    public async Task<WebhookHandlingResult> ApplyWebhookAsync(
        Shipment shipment,
        Providers.WebhookEvent canonical,
        CancellationToken ct)
    {
        var fromState = shipment.State;
        var proposed = MapCanonicalToInternalState(canonical.CanonicalEventKind);

        bool advanced = false;
        if (proposed is not null && ShipmentStateMachine.ShouldApply(fromState, proposed))
        {
            // System actor for webhook-driven transitions. AuditEventPublisher
            // validates non-empty; this stable sentinel uniquely identifies
            // provider-webhook origination across audit-log readers.
            await TransitionAsync(
                shipment: shipment,
                newState: proposed,
                actorId: SystemActor.WebhookSystemActorId,
                actorRole: "system",
                reason: $"webhook:{canonical.ProviderEventKind}",
                ct: ct);
            advanced = true;
        }

        // Always record the event in history (BR-12).
        db.ShipmentEvents.Add(new ShipmentEvent
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipment.Id,
            ProviderEventKind = canonical.ProviderEventKind,
            InternalStateAtEvent = shipment.State,
            OccurredAt = canonical.OccurredAt,
            ReceivedAt = clock.GetUtcNow(),
            RawPayloadRedactedJson = canonical.RawPayloadRedactedJson,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return new WebhookHandlingResult(advanced, fromState, shipment.State);
    }

    private static string? MapCanonicalToInternalState(string canonical) => canonical switch
    {
        Providers.CanonicalEventKinds.LabelPurchased => ShipmentStates.LabelPurchased,
        Providers.CanonicalEventKinds.HandedToCarrier => ShipmentStates.HandedToCarrier,
        Providers.CanonicalEventKinds.InTransit => ShipmentStates.InTransit,
        Providers.CanonicalEventKinds.OutForDelivery => ShipmentStates.OutForDelivery,
        Providers.CanonicalEventKinds.Delivered => ShipmentStates.Delivered,
        Providers.CanonicalEventKinds.DeliveryAttempted => ShipmentStates.DeliveryAttempted,
        Providers.CanonicalEventKinds.ReturnedToSender => ShipmentStates.ReturnedToSender,
        _ => null,
    };
}

public sealed record WebhookHandlingResult(bool Advanced, string FromState, string ToState);
