using BackendApi.Modules.Shipping.Domain.StateMachines;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Features.Methods;

public sealed record RejectMethodCommand(Guid MethodVersionId, Guid ReviewerId, string Reason) : IRequest;

public sealed class RejectMethodHandler(ShippingDbContext db, TimeProvider clock)
    : IRequestHandler<RejectMethodCommand>
{
    public async Task Handle(RejectMethodCommand cmd, CancellationToken ct)
    {
        var version = await db.ShippingMethodVersions
            .FirstOrDefaultAsync(v => v.Id == cmd.MethodVersionId, ct)
            ?? throw new InvalidOperationException("Method version not found.");
        MethodVersionStateMachine.EnsureTransition(version.State, MethodVersionStates.Draft);
        version.State = MethodVersionStates.Draft;
        version.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }
}
