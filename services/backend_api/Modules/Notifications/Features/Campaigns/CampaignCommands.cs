using BackendApi.Modules.Notifications.Domain;
using BackendApi.Modules.Notifications.Domain.StateMachines;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Notifications.Features.Campaigns;

/// <summary>
/// T036 — six campaign authoring commands (Create, Schedule, Pause, Resume,
/// Cancel, GetReport) on the <see cref="Campaign"/> state machine. All
/// transitions go through <see cref="CampaignStateMachine"/>; failure to
/// satisfy a transition surfaces as HTTP 409.
/// </summary>

public sealed record CreateCampaignCommand(
    string Name,
    Guid TemplateId,
    Guid? TemplateVersionId,
    string Channel,
    string MarketCode,
    string TargetCriteriaJson,
    DateTimeOffset? SendAt,
    Guid CreatedBy) : IRequest<Guid>;

public sealed record ScheduleCampaignCommand(Guid CampaignId, DateTimeOffset SendAt) : IRequest<Unit>;
public sealed record PauseCampaignCommand(Guid CampaignId) : IRequest<Unit>;
public sealed record ResumeCampaignCommand(Guid CampaignId) : IRequest<Unit>;
public sealed record CancelCampaignCommand(Guid CampaignId, string Reason) : IRequest<Unit>;
public sealed record GetCampaignReportQuery(Guid CampaignId) : IRequest<CampaignReport?>;

public sealed record CampaignReport(
    Guid CampaignId,
    string State,
    int? RecipientCountSnapshot,
    int Queued,
    int Delivered,
    int Failed,
    int DeadLetter,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed class CreateCampaignHandler : IRequestHandler<CreateCampaignCommand, Guid>
{
    private readonly NotificationsDbContext _db;
    private readonly TimeProvider _clock;

    public CreateCampaignHandler(NotificationsDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreateCampaignCommand request, CancellationToken ct)
    {
        if (request.Channel == NotificationsConstants.EventKinds.AuthOtpRequested)
            throw new InvalidOperationException("Campaigns MUST NOT target OTP (DB check constraint).");

        var now = _clock.GetUtcNow();
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            State = NotificationsConstants.CampaignStates.Draft,
            TemplateId = request.TemplateId,
            TemplateVersionId = request.TemplateVersionId,
            Channel = request.Channel,
            MarketCode = request.MarketCode,
            TargetCriteriaJson = request.TargetCriteriaJson,
            SendAt = request.SendAt,
            CreatedBy = request.CreatedBy,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync(ct);
        return campaign.Id;
    }
}

public sealed class ScheduleCampaignHandler : IRequestHandler<ScheduleCampaignCommand, Unit>
{
    private readonly NotificationsDbContext _db;
    private readonly TimeProvider _clock;

    public ScheduleCampaignHandler(NotificationsDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Unit> Handle(ScheduleCampaignCommand request, CancellationToken ct)
    {
        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == request.CampaignId, ct)
            ?? throw new InvalidOperationException("Campaign not found.");
        CampaignStateMachine.EnsureTransition(c.State, NotificationsConstants.CampaignStates.Scheduled);
        c.State = NotificationsConstants.CampaignStates.Scheduled;
        c.SendAt = request.SendAt;
        c.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public sealed class PauseCampaignHandler : IRequestHandler<PauseCampaignCommand, Unit>
{
    private readonly NotificationsDbContext _db;
    private readonly TimeProvider _clock;

    public PauseCampaignHandler(NotificationsDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Unit> Handle(PauseCampaignCommand request, CancellationToken ct)
    {
        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == request.CampaignId, ct)
            ?? throw new InvalidOperationException("Campaign not found.");
        CampaignStateMachine.EnsureTransition(c.State, NotificationsConstants.CampaignStates.Paused);
        c.State = NotificationsConstants.CampaignStates.Paused;
        c.PausedAt = _clock.GetUtcNow();
        c.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public sealed class ResumeCampaignHandler : IRequestHandler<ResumeCampaignCommand, Unit>
{
    private readonly NotificationsDbContext _db;
    private readonly TimeProvider _clock;

    public ResumeCampaignHandler(NotificationsDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Unit> Handle(ResumeCampaignCommand request, CancellationToken ct)
    {
        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == request.CampaignId, ct)
            ?? throw new InvalidOperationException("Campaign not found.");
        CampaignStateMachine.EnsureTransition(c.State, NotificationsConstants.CampaignStates.Sending);
        c.State = NotificationsConstants.CampaignStates.Sending;
        c.PausedAt = null;
        c.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public sealed class CancelCampaignHandler : IRequestHandler<CancelCampaignCommand, Unit>
{
    private readonly NotificationsDbContext _db;
    private readonly TimeProvider _clock;

    public CancelCampaignHandler(NotificationsDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Unit> Handle(CancelCampaignCommand request, CancellationToken ct)
    {
        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == request.CampaignId, ct)
            ?? throw new InvalidOperationException("Campaign not found.");
        CampaignStateMachine.EnsureTransition(c.State, NotificationsConstants.CampaignStates.Cancelled);
        c.State = NotificationsConstants.CampaignStates.Cancelled;
        c.CancelledAt = _clock.GetUtcNow();
        c.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public sealed class GetCampaignReportHandler : IRequestHandler<GetCampaignReportQuery, CampaignReport?>
{
    private readonly NotificationsDbContext _db;

    public GetCampaignReportHandler(NotificationsDbContext db) { _db = db; }

    public async Task<CampaignReport?> Handle(GetCampaignReportQuery request, CancellationToken ct)
    {
        var c = await _db.Campaigns.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.CampaignId, ct);
        if (c is null) return null;

        var grouped = await _db.Notifications.AsNoTracking()
            .Where(n => n.CampaignId == request.CampaignId)
            .GroupBy(n => n.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int CountIn(string state) => grouped.FirstOrDefault(x => x.State == state)?.Count ?? 0;
        return new CampaignReport(
            CampaignId: c.Id,
            State: c.State,
            RecipientCountSnapshot: c.RecipientCountSnapshot,
            Queued: CountIn(NotificationsConstants.NotificationStates.Pending)
                  + CountIn(NotificationsConstants.NotificationStates.Queued)
                  + CountIn(NotificationsConstants.NotificationStates.Sending),
            Delivered: CountIn(NotificationsConstants.NotificationStates.Delivered),
            Failed: CountIn(NotificationsConstants.NotificationStates.Failed),
            DeadLetter: CountIn(NotificationsConstants.NotificationStates.DeadLetter),
            StartedAt: c.StartedAt,
            CompletedAt: c.CompletedAt);
    }
}
