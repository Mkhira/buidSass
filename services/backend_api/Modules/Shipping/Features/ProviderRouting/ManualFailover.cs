using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shipping.Domain.Events;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Features.ProviderRouting;

public sealed record ManualFailoverCommand(
    string MarketCode,
    Guid MethodId,
    Guid ActorId) : IRequest;

/// <summary>
/// AC-19 — operator-triggered failover swaps primary ↔ backup. Emits
/// `provider.failover` audit event + MediatR notification. After this
/// returns, new shipments use the swapped primary; the
/// <c>ReattemptQueuedLabelsWorker</c> handles pending failures.
/// </summary>
public sealed class ManualFailoverHandler(
    ShippingDbContext db,
    IAuditEventPublisher audit,
    IPublisher events,
    TimeProvider clock) : IRequestHandler<ManualFailoverCommand>
{
    public async Task Handle(ManualFailoverCommand cmd, CancellationToken ct)
    {
        var row = await db.ProviderRoutings
            .FirstOrDefaultAsync(r => r.MarketCode == cmd.MarketCode && r.MethodId == cmd.MethodId, ct)
            ?? throw new InvalidOperationException("Routing row not found.");

        if (string.IsNullOrWhiteSpace(row.BackupProviderId))
        {
            throw new InvalidOperationException("No backup provider configured; failover impossible.");
        }

        var oldPrimary = row.PrimaryProviderId;
        var oldBackup = row.BackupProviderId!;
        row.PrimaryProviderId = oldBackup;
        row.BackupProviderId = oldPrimary;
        var nowUtc = clock.GetUtcNow();
        row.UpdatedAt = nowUtc;
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            ActorId: cmd.ActorId,
            ActorRole: "shipping-operator",
            Action: ShippingConstants.AuditActions.ProviderFailover,
            EntityType: ShippingConstants.EntityTypes.ProviderRouting,
            EntityId: cmd.MethodId,
            BeforeState: new { primary = oldPrimary, backup = oldBackup },
            AfterState: new { primary = oldBackup, backup = oldPrimary },
            Reason: "manual_operator_failover"), ct);

        await events.Publish(new ProviderFailoverPerformed(
            cmd.MarketCode, cmd.MethodId, oldPrimary, oldBackup, nowUtc), ct);
    }
}
