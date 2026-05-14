using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shipping.Domain.StateMachines;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Features.Methods;

public sealed record ApproveMethodCommand(Guid MethodVersionId, Guid ReviewerId) : IRequest;

/// <summary>
/// V-1 publish gate enforcement (AC-14 / AC-15):
///  - AR and EN names must be non-empty on the owning method.
///  - Reviewer must differ from the author.
///  - Method's current version pointer is updated and any prior published
///    version is auto-archived (Flow 4).
/// Audit emits <c>shipping.method_published</c> + (for any auto-archived
/// predecessor) <c>shipping.method_archived</c>.
/// </summary>
public sealed class ApproveMethodHandler(
    ShippingDbContext db,
    IAuditEventPublisher audit,
    TimeProvider clock) : IRequestHandler<ApproveMethodCommand>
{
    public async Task Handle(ApproveMethodCommand cmd, CancellationToken ct)
    {
        if (cmd.ReviewerId == Guid.Empty)
        {
            throw new ArgumentException("ReviewerId required", nameof(cmd));
        }
        var version = await db.ShippingMethodVersions
            .FirstOrDefaultAsync(v => v.Id == cmd.MethodVersionId, ct)
            ?? throw new InvalidOperationException("Method version not found.");

        // AC-15 — reviewer must differ from author.
        if (cmd.ReviewerId == version.AuthorId)
        {
            throw new ShippingPublishGateException("Reviewer must differ from author (V-1).");
        }

        var method = await db.ShippingMethods
            .FirstOrDefaultAsync(m => m.Id == version.MethodId, ct)
            ?? throw new InvalidOperationException("Owning method not found.");

        // AC-14 — V-1 publish gate: AR + EN names required (Principle 4).
        if (string.IsNullOrWhiteSpace(method.NameAr) || string.IsNullOrWhiteSpace(method.NameEn))
        {
            throw new ShippingPublishGateException("Both AR and EN names are required (V-1 / Principle 4).");
        }

        MethodVersionStateMachine.EnsureTransition(version.State, MethodVersionStates.Published);

        var nowUtc = clock.GetUtcNow();
        version.State = MethodVersionStates.Published;
        version.ReviewerId = cmd.ReviewerId;
        version.PublishedAt = nowUtc;
        version.UpdatedAt = nowUtc;

        // Auto-archive the previously-published version of the same method.
        var prior = await db.ShippingMethodVersions
            .Where(v => v.MethodId == method.Id
                && v.State == MethodVersionStates.Published
                && v.Id != version.Id)
            .ToListAsync(ct);
        foreach (var p in prior)
        {
            p.State = MethodVersionStates.Archived;
            p.ArchivedAt = nowUtc;
            p.UpdatedAt = nowUtc;
        }

        method.CurrentVersionId = version.Id;
        method.UpdatedAt = nowUtc;
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            ActorId: cmd.ReviewerId,
            ActorRole: "shipping-operator",
            Action: ShippingConstants.AuditActions.MethodPublished,
            EntityType: ShippingConstants.EntityTypes.ShippingMethodVersion,
            EntityId: version.Id,
            BeforeState: new { state = MethodVersionStates.InReview },
            AfterState: new { state = MethodVersionStates.Published, version_no = version.VersionNo },
            Reason: null), ct);

        foreach (var p in prior)
        {
            await audit.PublishAsync(new AuditEvent(
                ActorId: cmd.ReviewerId,
                ActorRole: "shipping-operator",
                Action: ShippingConstants.AuditActions.MethodArchived,
                EntityType: ShippingConstants.EntityTypes.ShippingMethodVersion,
                EntityId: p.Id,
                BeforeState: new { state = MethodVersionStates.Published },
                AfterState: new { state = MethodVersionStates.Archived },
                Reason: "superseded_by_new_publish"), ct);
        }
    }
}
