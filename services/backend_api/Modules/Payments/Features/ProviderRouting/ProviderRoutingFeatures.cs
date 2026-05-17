using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Providers;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Payments.Features.ProviderRouting;

/// <summary>T060 — GET/PUT provider routing + manual failover endpoints.</summary>
public sealed record ListProviderRoutingQuery() : IRequest<IReadOnlyList<ProviderRoutingDto>>;

public sealed record ProviderRoutingDto(
    string MarketCode, string Method, string PrimaryProviderId, string? BackupProviderId,
    bool AutoFailoverEnabled, int FailoverThresholdPct, int FailoverWindowMinutes);

public sealed class ListProviderRoutingHandler : IRequestHandler<ListProviderRoutingQuery, IReadOnlyList<ProviderRoutingDto>>
{
    private readonly PaymentsDbContext _db;
    public ListProviderRoutingHandler(PaymentsDbContext db) { _db = db; }

    public async Task<IReadOnlyList<ProviderRoutingDto>> Handle(ListProviderRoutingQuery req, CancellationToken ct)
    {
        return await _db.ProviderRoutings
            .OrderBy(r => r.MarketCode).ThenBy(r => r.Method)
            .Select(r => new ProviderRoutingDto(
                r.MarketCode, r.Method, r.PrimaryProviderId, r.BackupProviderId,
                r.AutoFailoverEnabled, r.FailoverThresholdPct, r.FailoverWindowMinutes))
            .ToListAsync(ct);
    }
}

public sealed record UpdateProviderRoutingCommand(
    string MarketCode, string Method, string PrimaryProviderId, string? BackupProviderId,
    bool AutoFailoverEnabled, int FailoverThresholdPct, int FailoverWindowMinutes,
    Guid OperatorId) : IRequest;

public sealed class UpdateProviderRoutingHandler : IRequestHandler<UpdateProviderRoutingCommand>
{
    private readonly PaymentsDbContext _db;
    private readonly ProviderRegistry _providers;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public UpdateProviderRoutingHandler(
        PaymentsDbContext db,
        ProviderRegistry providers,
        IAuditEventPublisher audit,
        TimeProvider clock)
    {
        _db = db; _providers = providers; _audit = audit; _clock = clock;
    }

    public async Task Handle(UpdateProviderRoutingCommand cmd, CancellationToken ct)
    {
        if (!PaymentsConstants.Markets.IsValid(cmd.MarketCode))
            throw new InvalidOperationException("Invalid market");
        if (!PaymentsConstants.Methods.All.Contains(cmd.Method))
            throw new InvalidOperationException("Invalid method");
        if (cmd.PrimaryProviderId == cmd.BackupProviderId)
            throw new InvalidOperationException("Primary and backup providers must differ (V-6)");
        if (!_providers.TryResolve(cmd.PrimaryProviderId, out var primary) || primary is null)
            throw new InvalidOperationException("Unknown primary provider");
        if (!primary.SupportsMarket(cmd.MarketCode) || !primary.SupportsMethod(cmd.Method))
            throw new InvalidOperationException("Primary provider does not support market+method");
        // Validate backup provider capability at update-time. Failover later
        // promotes backup → primary and we must never advertise a routing whose
        // backup is incompatible with the (market, method) pair.
        if (cmd.BackupProviderId is not null)
        {
            if (!_providers.TryResolve(cmd.BackupProviderId, out var backup) || backup is null)
                throw new InvalidOperationException("Unknown backup provider");
            if (!backup.SupportsMarket(cmd.MarketCode) || !backup.SupportsMethod(cmd.Method))
                throw new InvalidOperationException("Backup provider does not support market+method");
        }
        // BR-13 + DB CK_provider_routing_auto_failover_requires_backup —
        // enforce at the app layer too so the caller sees a clean 400 rather
        // than a check-violation 500.
        if (cmd.AutoFailoverEnabled && cmd.BackupProviderId is null)
            throw new InvalidOperationException("AutoFailoverEnabled requires a BackupProviderId");
        // Bound checks for the failover-window dials. The DB CHECK constraints
        // (FailoverThresholdPct BETWEEN 1 AND 100; FailoverWindowMinutes > 0)
        // also enforce these; the app-layer check yields a clean 400 instead
        // of a generic check-violation 500.
        if (cmd.FailoverThresholdPct < 1 || cmd.FailoverThresholdPct > 100)
            throw new InvalidOperationException("FailoverThresholdPct must be in [1, 100]");
        // Cap window at 24h — anything longer is operationally meaningless
        // for a per-routing failover decision.
        if (cmd.FailoverWindowMinutes < 1 || cmd.FailoverWindowMinutes > 1440)
            throw new InvalidOperationException("FailoverWindowMinutes must be in [1, 1440]");

        var row = await _db.ProviderRoutings.FirstOrDefaultAsync(
            r => r.MarketCode == cmd.MarketCode && r.Method == cmd.Method, ct);
        var before = row is null ? null : new
        {
            primary = row.PrimaryProviderId,
            backup = row.BackupProviderId,
            auto = row.AutoFailoverEnabled,
            threshold = row.FailoverThresholdPct,
            window = row.FailoverWindowMinutes,
        };
        if (row is null)
        {
            row = new Domain.ProviderRouting { MarketCode = cmd.MarketCode, Method = cmd.Method };
            _db.ProviderRoutings.Add(row);
        }
        row.PrimaryProviderId = cmd.PrimaryProviderId;
        row.BackupProviderId = cmd.BackupProviderId;
        row.AutoFailoverEnabled = cmd.AutoFailoverEnabled;
        row.FailoverThresholdPct = cmd.FailoverThresholdPct;
        row.FailoverWindowMinutes = cmd.FailoverWindowMinutes;
        row.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);

        // Principle 25 audit — record the operator-driven routing change.
        await _audit.PublishAsync(new AuditEvent(
            ActorId: cmd.OperatorId, ActorRole: "payments-operator",
            Action: "payments.provider_routing.updated",
            EntityType: "ProviderRouting", EntityId: Guid.Empty,
            BeforeState: before,
            AfterState: new
            {
                primary = row.PrimaryProviderId,
                backup = row.BackupProviderId,
                auto = row.AutoFailoverEnabled,
                threshold = row.FailoverThresholdPct,
                window = row.FailoverWindowMinutes,
            },
            Reason: $"update routing {cmd.MarketCode}/{cmd.Method}"), ct);
    }
}

public sealed record FailoverProviderCommand(string MarketCode, string Method, Guid OperatorId) : IRequest;

public sealed class FailoverProviderHandler : IRequestHandler<FailoverProviderCommand>
{
    private readonly PaymentsDbContext _db;
    private readonly ProviderRegistry _providers;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public FailoverProviderHandler(
        PaymentsDbContext db,
        ProviderRegistry providers,
        IAuditEventPublisher audit,
        TimeProvider clock)
    {
        _db = db; _providers = providers; _audit = audit; _clock = clock;
    }

    public async Task Handle(FailoverProviderCommand cmd, CancellationToken ct)
    {
        var row = await _db.ProviderRoutings.FirstOrDefaultAsync(
            r => r.MarketCode == cmd.MarketCode && r.Method == cmd.Method, ct)
            ?? throw new InvalidOperationException("Routing not found");
        if (string.IsNullOrEmpty(row.BackupProviderId))
            throw new InvalidOperationException("No backup provider configured");

        // Re-validate the backup at failover-time: an admin may have updated
        // the supported-method matrix between the update-time check and now,
        // and we MUST never promote an incompatible provider to primary.
        if (!_providers.TryResolve(row.BackupProviderId, out var backup) || backup is null)
            throw new InvalidOperationException("Backup provider no longer registered");
        if (!backup.SupportsMarket(cmd.MarketCode) || !backup.SupportsMethod(cmd.Method))
            throw new InvalidOperationException("Backup provider no longer supports market+method");

        var before = new { primary = row.PrimaryProviderId, backup = row.BackupProviderId };
        (row.PrimaryProviderId, row.BackupProviderId) = (row.BackupProviderId, row.PrimaryProviderId);
        row.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        var after = new { primary = row.PrimaryProviderId, backup = row.BackupProviderId };
        await _audit.PublishAsync(new AuditEvent(
            ActorId: cmd.OperatorId, ActorRole: "payments-operator",
            Action: PaymentsConstants.AuditActions.ProviderFailover,
            EntityType: "ProviderRouting", EntityId: Guid.Empty,
            BeforeState: before, AfterState: after,
            Reason: $"manual failover {cmd.MarketCode}/{cmd.Method}"), ct);
    }
}
