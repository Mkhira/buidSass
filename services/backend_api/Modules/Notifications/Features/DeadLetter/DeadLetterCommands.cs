using BackendApi.Modules.Notifications.Domain;
using BackendApi.Modules.Notifications.Domain.StateMachines;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Notifications.Features.DeadLetter;

/// <summary>
/// T045 — operator surfaces over the dead-letter queue. List supports paging
/// by EnteredAt desc; RetryNow flips Notification.state back to retrying so
/// the dispatch worker re-attempts; Discard records a final resolution.
/// </summary>

public sealed record ListDeadLetterQuery(int Skip = 0, int Take = 50) : IRequest<IReadOnlyList<DeadLetterView>>;
public sealed record DeadLetterView(
    Guid NotificationId,
    string Channel,
    string MarketCode,
    string EventKind,
    string? LastErrorMessageRedacted,
    DateTimeOffset EnteredAt,
    DateTimeOffset? ResolvedAt,
    string? Resolution);

public sealed record RetryDeadLetterCommand(Guid NotificationId, Guid OperatorId) : IRequest<bool>;
public sealed record DiscardDeadLetterCommand(Guid NotificationId, Guid OperatorId, string ReasonNote) : IRequest<bool>;

public sealed class ListDeadLetterHandler : IRequestHandler<ListDeadLetterQuery, IReadOnlyList<DeadLetterView>>
{
    private readonly NotificationsDbContext _db;

    public ListDeadLetterHandler(NotificationsDbContext db) { _db = db; }

    public async Task<IReadOnlyList<DeadLetterView>> Handle(ListDeadLetterQuery request, CancellationToken ct)
    {
        var rows = await (
            from d in _db.DeadLetterEntries.AsNoTracking()
            join n in _db.Notifications.AsNoTracking() on d.NotificationId equals n.Id
            where d.ResolvedAt == null
            orderby d.EnteredAt descending
            select new DeadLetterView(
                d.NotificationId, n.Channel, n.MarketCode, n.EventKind,
                d.LastErrorMessageRedacted, d.EnteredAt, d.ResolvedAt, d.Resolution))
            .Skip(request.Skip).Take(request.Take).ToListAsync(ct);
        return rows;
    }
}

public sealed class RetryDeadLetterHandler : IRequestHandler<RetryDeadLetterCommand, bool>
{
    private readonly NotificationsDbContext _db;
    private readonly TimeProvider _clock;

    public RetryDeadLetterHandler(NotificationsDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<bool> Handle(RetryDeadLetterCommand request, CancellationToken ct)
    {
        var dl = await _db.DeadLetterEntries
            .FirstOrDefaultAsync(d => d.NotificationId == request.NotificationId && d.ResolvedAt == null, ct);
        if (dl is null) return false;
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == request.NotificationId, ct);
        if (n is null) return false;

        // Reset attempts so the next worker iteration starts fresh.
        n.Attempts = 0;
        NotificationStateMachine.EnsureTransition(n.State, NotificationsConstants.NotificationStates.Retrying);
        n.State = NotificationsConstants.NotificationStates.Retrying;
        n.UpdatedAt = _clock.GetUtcNow();

        dl.ResolvedAt = _clock.GetUtcNow();
        dl.Resolution = "retried";
        dl.ResolvedBy = request.OperatorId;
        dl.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class DiscardDeadLetterHandler : IRequestHandler<DiscardDeadLetterCommand, bool>
{
    private readonly NotificationsDbContext _db;
    private readonly TimeProvider _clock;

    public DiscardDeadLetterHandler(NotificationsDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<bool> Handle(DiscardDeadLetterCommand request, CancellationToken ct)
    {
        var dl = await _db.DeadLetterEntries
            .FirstOrDefaultAsync(d => d.NotificationId == request.NotificationId && d.ResolvedAt == null, ct);
        if (dl is null) return false;

        dl.ResolvedAt = _clock.GetUtcNow();
        dl.Resolution = "discarded:" + request.ReasonNote;
        dl.ResolvedBy = request.OperatorId;
        dl.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
