using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shipping.Domain;
using BackendApi.Modules.Shipping.Domain.Events;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using BackendApi.Modules.Shipping.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Features.Shipments;

public sealed record CreateReDeliveryCommand(Guid ParentShipmentId, Guid ActorId) : IRequest<Guid>;

public sealed class CreateReDeliveryHandler(
    ShippingDbContext db,
    ShipmentService shipments,
    IAuditEventPublisher audit,
    IPublisher events,
    TimeProvider clock) : IRequestHandler<CreateReDeliveryCommand, Guid>
{
    public async Task<Guid> Handle(CreateReDeliveryCommand cmd, CancellationToken ct)
    {
        var parent = await db.Shipments.FirstOrDefaultAsync(s => s.Id == cmd.ParentShipmentId, ct)
            ?? throw new InvalidOperationException("Parent shipment not found.");
        if (parent.State != ShipmentStates.DeliveryDisputed)
        {
            throw new InvalidOperationException(
                "Re-delivery can only be created from a delivery_disputed shipment.");
        }

        var nowUtc = clock.GetUtcNow();
        var newShipment = new Shipment
        {
            Id = Guid.NewGuid(),
            OrderId = parent.OrderId,
            MarketCode = parent.MarketCode,
            MethodVersionId = parent.MethodVersionId,
            ProviderId = parent.ProviderId,
            State = ShipmentStates.Pending,
            ShipToAddressRedactedJson = parent.ShipToAddressRedactedJson,
            ParentShipmentId = parent.Id,
            Attempts = 0,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };

        // Soft-conflict with the partial unique index on order_id: we mark
        // the parent as re_delivered_pending FIRST so its (order_id) slot is
        // freed for the new active row.
        await shipments.TransitionAsync(
            shipment: parent,
            newState: ShipmentStates.ReDeliveredPending,
            actorId: cmd.ActorId,
            actorRole: "shipping-operator",
            reason: "re_delivery_created",
            ct: ct);

        db.Shipments.Add(newShipment);
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            ActorId: cmd.ActorId,
            ActorRole: "shipping-operator",
            Action: ShippingConstants.AuditActions.ReDeliveryCreated,
            EntityType: ShippingConstants.EntityTypes.Shipment,
            EntityId: newShipment.Id,
            BeforeState: null,
            AfterState: new { parent_shipment_id = parent.Id },
            Reason: null), ct);

        await events.Publish(new ShipmentReDeliveryCreated(
            newShipment.Id, parent.Id, parent.OrderId, parent.MarketCode, nowUtc), ct);

        return newShipment.Id;
    }
}
