using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shipping.Domain;
using BackendApi.Modules.Shipping.Domain.Events;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using BackendApi.Modules.Shipping.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Features.Shipments;

public sealed record DisputeShipmentCommand(
    Guid ShipmentId,
    Guid ActorId,
    string ReportedBy,
    string? Notes) : IRequest<Guid>;

public sealed class DisputeShipmentHandler(
    ShippingDbContext db,
    ShipmentService shipments,
    IAuditEventPublisher audit,
    IPublisher events,
    TimeProvider clock) : IRequestHandler<DisputeShipmentCommand, Guid>
{
    public async Task<Guid> Handle(DisputeShipmentCommand cmd, CancellationToken ct)
    {
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == cmd.ShipmentId, ct)
            ?? throw new InvalidOperationException("Shipment not found.");
        if (shipment.State != ShipmentStates.Delivered)
        {
            throw new InvalidOperationException("Only delivered shipments can be disputed.");
        }
        var dispute = new ShipmentDispute
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipment.Id,
            ReportedAt = clock.GetUtcNow(),
            ReportedBy = cmd.ReportedBy,
            Status = ShipmentDisputeStatuses.Open,
            ResolutionNotes = cmd.Notes,
        };
        db.ShipmentDisputes.Add(dispute);
        await db.SaveChangesAsync(ct);

        await shipments.TransitionAsync(
            shipment: shipment,
            newState: ShipmentStates.DeliveryDisputed,
            actorId: cmd.ActorId,
            actorRole: "shipping-operator",
            reason: "operator_dispute_marked",
            ct: ct);

        await audit.PublishAsync(new AuditEvent(
            ActorId: cmd.ActorId,
            ActorRole: "shipping-operator",
            Action: ShippingConstants.AuditActions.DeliveryDisputed,
            EntityType: ShippingConstants.EntityTypes.Shipment,
            EntityId: shipment.Id,
            BeforeState: null,
            AfterState: new { dispute_id = dispute.Id, reported_by = cmd.ReportedBy },
            Reason: cmd.Notes), ct);

        await events.Publish(new ShipmentDeliveryDisputed(
            shipment.Id, shipment.OrderId, shipment.MarketCode, clock.GetUtcNow()), ct);

        return dispute.Id;
    }
}
