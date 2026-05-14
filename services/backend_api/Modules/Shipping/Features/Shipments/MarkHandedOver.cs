using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using BackendApi.Modules.Shipping.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Features.Shipments;

public sealed record MarkHandedOverCommand(Guid ShipmentId, Guid ActorId) : IRequest;

public sealed class MarkHandedOverHandler(
    ShippingDbContext db,
    ShipmentService shipments) : IRequestHandler<MarkHandedOverCommand>
{
    public async Task Handle(MarkHandedOverCommand cmd, CancellationToken ct)
    {
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == cmd.ShipmentId, ct)
            ?? throw new InvalidOperationException("Shipment not found.");
        await shipments.TransitionAsync(
            shipment: shipment,
            newState: ShipmentStates.HandedToCarrier,
            actorId: cmd.ActorId,
            actorRole: "warehouse-staff",
            reason: "warehouse_handover",
            ct: ct);
    }
}
