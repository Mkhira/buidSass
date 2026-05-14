using BackendApi.Modules.Shipping.Domain.StateMachines;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Features.Methods;

public sealed record SubmitForReviewCommand(Guid MethodVersionId, Guid ActorId) : IRequest;

public sealed class SubmitForReviewHandler(ShippingDbContext db, TimeProvider clock)
    : IRequestHandler<SubmitForReviewCommand>
{
    public async Task Handle(SubmitForReviewCommand cmd, CancellationToken ct)
    {
        var version = await db.ShippingMethodVersions
            .FirstOrDefaultAsync(v => v.Id == cmd.MethodVersionId, ct)
            ?? throw new InvalidOperationException("Method version not found.");
        MethodVersionStateMachine.EnsureTransition(version.State, MethodVersionStates.InReview);
        version.State = MethodVersionStates.InReview;
        version.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }
}
