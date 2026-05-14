using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shipping.Domain.StateMachines;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Features.Methods;

public sealed record RejectMethodCommand(Guid MethodVersionId, Guid ReviewerId, string Reason) : IRequest;

/// <summary>
/// Rejects a method-version under review back to <c>draft</c>. The
/// reviewer must differ from the author (V-1 / AC-15 — same gate as
/// publish) and is recorded on the version row so the rejection trail
/// is queryable. Emits a <c>shipping.method_archived</c>-shaped audit
/// row with reason=<c>rejected:&lt;reason&gt;</c> so downstream auditors
/// can distinguish rejection from operator-driven archive.
/// </summary>
public sealed class RejectMethodHandler(
    ShippingDbContext db,
    IAuditEventPublisher audit,
    TimeProvider clock) : IRequestHandler<RejectMethodCommand>
{
    public async Task Handle(RejectMethodCommand cmd, CancellationToken ct)
    {
        if (cmd.ReviewerId == Guid.Empty)
        {
            throw new ArgumentException("ReviewerId required", nameof(cmd));
        }
        var version = await db.ShippingMethodVersions
            .FirstOrDefaultAsync(v => v.Id == cmd.MethodVersionId, ct)
            ?? throw new InvalidOperationException("Method version not found.");

        // V-1 — same gate as publish; the author cannot self-review.
        if (cmd.ReviewerId == version.AuthorId)
        {
            throw new ShippingPublishGateException("Reviewer must differ from author (V-1).");
        }

        MethodVersionStateMachine.EnsureTransition(version.State, MethodVersionStates.Draft);
        var nowUtc = clock.GetUtcNow();
        var before = version.State;
        version.State = MethodVersionStates.Draft;
        version.ReviewerId = cmd.ReviewerId;
        version.UpdatedAt = nowUtc;
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            ActorId: cmd.ReviewerId,
            ActorRole: "shipping-operator",
            Action: ShippingConstants.AuditActions.MethodArchived,
            EntityType: ShippingConstants.EntityTypes.ShippingMethodVersion,
            EntityId: version.Id,
            BeforeState: new { state = before },
            AfterState: new { state = MethodVersionStates.Draft, reviewer_id = cmd.ReviewerId },
            Reason: $"rejected:{cmd.Reason}"), ct);
    }
}
