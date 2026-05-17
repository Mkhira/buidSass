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
    private readonly TimeProvider _clock;

    public UpdateProviderRoutingHandler(PaymentsDbContext db, ProviderRegistry providers, TimeProvider clock)
    {
        _db = db; _providers = providers; _clock = clock;
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

        var row = await _db.ProviderRoutings.FirstOrDefaultAsync(
            r => r.MarketCode == cmd.MarketCode && r.Method == cmd.Method, ct);
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
    }
}

public sealed record FailoverProviderCommand(string MarketCode, string Method, Guid OperatorId) : IRequest;

public sealed class FailoverProviderHandler : IRequestHandler<FailoverProviderCommand>
{
    private readonly PaymentsDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public FailoverProviderHandler(PaymentsDbContext db, IAuditEventPublisher audit, TimeProvider clock)
    {
        _db = db; _audit = audit; _clock = clock;
    }

    public async Task Handle(FailoverProviderCommand cmd, CancellationToken ct)
    {
        var row = await _db.ProviderRoutings.FirstOrDefaultAsync(
            r => r.MarketCode == cmd.MarketCode && r.Method == cmd.Method, ct)
            ?? throw new InvalidOperationException("Routing not found");
        if (string.IsNullOrEmpty(row.BackupProviderId))
            throw new InvalidOperationException("No backup provider configured");

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
