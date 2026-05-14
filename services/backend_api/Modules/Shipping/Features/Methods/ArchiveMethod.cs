using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shipping.Domain.StateMachines;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Features.Methods;

public sealed record ArchiveMethodCommand(Guid MethodVersionId, Guid ActorId) : IRequest;

public sealed class ArchiveMethodHandler(
    ShippingDbContext db,
    IAuditEventPublisher audit,
    TimeProvider clock) : IRequestHandler<ArchiveMethodCommand>
{
    public async Task Handle(ArchiveMethodCommand cmd, CancellationToken ct)
    {
        var version = await db.ShippingMethodVersions
            .FirstOrDefaultAsync(v => v.Id == cmd.MethodVersionId, ct)
            ?? throw new InvalidOperationException("Method version not found.");
        var before = version.State;
        MethodVersionStateMachine.EnsureTransition(before, MethodVersionStates.Archived);
        var nowUtc = clock.GetUtcNow();
        version.State = MethodVersionStates.Archived;
        version.ArchivedAt = nowUtc;
        version.UpdatedAt = nowUtc;
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            ActorId: cmd.ActorId,
            ActorRole: "shipping-operator",
            Action: ShippingConstants.AuditActions.MethodArchived,
            EntityType: ShippingConstants.EntityTypes.ShippingMethodVersion,
            EntityId: version.Id,
            BeforeState: new { state = before },
            AfterState: new { state = MethodVersionStates.Archived },
            Reason: "operator_archive"), ct);
    }
}
