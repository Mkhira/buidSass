using BackendApi.Modules.Notifications.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Notifications.Features.ProviderRouting;

/// <summary>
/// T046 — operator surfaces over per-(market, channel) provider routing.
/// Get returns the active row (or empty default if none configured); Set
/// upserts the row; Failover atomically swaps Primary ↔ Backup for crisis
/// response while preserving the same row identity. AutoFailoverEnabled
/// defaults false at v1 (clarify-locked); operators flip it on per row.
/// </summary>

public sealed record GetProviderRoutingQuery(string MarketCode, string Channel)
    : IRequest<ProviderRoutingView?>;
public sealed record ProviderRoutingView(
    string MarketCode,
    string Channel,
    string PrimaryProviderId,
    string? BackupProviderId,
    bool AutoFailoverEnabled,
    int FailoverThresholdPct,
    int FailoverWindowMinutes,
    DateTimeOffset UpdatedAt);

public sealed record SetProviderRoutingCommand(
    string MarketCode,
    string Channel,
    string PrimaryProviderId,
    string? BackupProviderId,
    bool AutoFailoverEnabled,
    int FailoverThresholdPct,
    int FailoverWindowMinutes,
    Guid OperatorId) : IRequest<Unit>;

public sealed record FailoverProviderRoutingCommand(string MarketCode, string Channel, Guid OperatorId)
    : IRequest<bool>;

public sealed class GetProviderRoutingHandler : IRequestHandler<GetProviderRoutingQuery, ProviderRoutingView?>
{
    private readonly NotificationsDbContext _db;

    public GetProviderRoutingHandler(NotificationsDbContext db) { _db = db; }

    public async Task<ProviderRoutingView?> Handle(GetProviderRoutingQuery request, CancellationToken ct)
    {
        var row = await _db.ProviderRoutings.AsNoTracking()
            .FirstOrDefaultAsync(p => p.MarketCode == request.MarketCode && p.Channel == request.Channel, ct);
        if (row is null) return null;
        return new ProviderRoutingView(
            row.MarketCode, row.Channel, row.PrimaryProviderId, row.BackupProviderId,
            row.AutoFailoverEnabled, row.FailoverThresholdPct, row.FailoverWindowMinutes, row.UpdatedAt);
    }
}

public sealed class SetProviderRoutingHandler : IRequestHandler<SetProviderRoutingCommand, Unit>
{
    private readonly NotificationsDbContext _db;
    private readonly TimeProvider _clock;

    public SetProviderRoutingHandler(NotificationsDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Unit> Handle(SetProviderRoutingCommand request, CancellationToken ct)
    {
        var row = await _db.ProviderRoutings.FirstOrDefaultAsync(
            p => p.MarketCode == request.MarketCode && p.Channel == request.Channel, ct);

        if (row is null)
        {
            _db.ProviderRoutings.Add(new Domain.ProviderRouting
            {
                MarketCode = request.MarketCode,
                Channel = request.Channel,
                PrimaryProviderId = request.PrimaryProviderId,
                BackupProviderId = request.BackupProviderId,
                AutoFailoverEnabled = request.AutoFailoverEnabled,
                FailoverThresholdPct = request.FailoverThresholdPct,
                FailoverWindowMinutes = request.FailoverWindowMinutes,
                UpdatedAt = _clock.GetUtcNow(),
            });
        }
        else
        {
            row.PrimaryProviderId = request.PrimaryProviderId;
            row.BackupProviderId = request.BackupProviderId;
            row.AutoFailoverEnabled = request.AutoFailoverEnabled;
            row.FailoverThresholdPct = request.FailoverThresholdPct;
            row.FailoverWindowMinutes = request.FailoverWindowMinutes;
            row.UpdatedAt = _clock.GetUtcNow();
        }
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public sealed class FailoverProviderRoutingHandler : IRequestHandler<FailoverProviderRoutingCommand, bool>
{
    private readonly NotificationsDbContext _db;
    private readonly TimeProvider _clock;

    public FailoverProviderRoutingHandler(NotificationsDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<bool> Handle(FailoverProviderRoutingCommand request, CancellationToken ct)
    {
        var row = await _db.ProviderRoutings.FirstOrDefaultAsync(
            p => p.MarketCode == request.MarketCode && p.Channel == request.Channel, ct);
        if (row is null || string.IsNullOrEmpty(row.BackupProviderId)) return false;

        (row.PrimaryProviderId, row.BackupProviderId) = (row.BackupProviderId!, row.PrimaryProviderId);
        row.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
