using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using BackendApi.Modules.Shipping.Providers;
using BackendApi.Modules.Shipping.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Features.Shipments;

public sealed record VoidLabelCommand(Guid ShipmentId, Guid ActorId, string Reason) : IRequest;

public sealed class VoidLabelHandler(
    ShippingDbContext db,
    ShipmentService shipments,
    ProviderRegistry providers,
    IAuditEventPublisher audit) : IRequestHandler<VoidLabelCommand>
{
    public async Task Handle(VoidLabelCommand cmd, CancellationToken ct)
    {
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == cmd.ShipmentId, ct)
            ?? throw new InvalidOperationException("Shipment not found.");
        if (ShipmentStates.IsTerminal(shipment.State))
        {
            throw new InvalidOperationException("Cannot void a terminal-state shipment.");
        }
        if (!string.IsNullOrEmpty(shipment.ProviderTrackingId)
            && providers.TryResolve(shipment.ProviderId, out var provider)
            && provider is not null)
        {
            await provider.VoidLabelAsync(shipment.ProviderTrackingId, ct);
        }
        await shipments.TransitionAsync(
            shipment: shipment,
            newState: ShipmentStates.LabelVoided,
            actorId: cmd.ActorId,
            actorRole: "shipping-operator",
            reason: cmd.Reason,
            ct: ct);
        await audit.PublishAsync(new AuditEvent(
            ActorId: cmd.ActorId,
            ActorRole: "shipping-operator",
            Action: ShippingConstants.AuditActions.LabelVoided,
            EntityType: ShippingConstants.EntityTypes.Shipment,
            EntityId: shipment.Id,
            BeforeState: null,
            AfterState: null,
            Reason: cmd.Reason), ct);
    }
}
