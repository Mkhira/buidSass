using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Publisher.ArchiveContent;

/// <summary>
/// POST /v1/admin/cms/banner-slots/{id}/archive per spec 024 contract §4.3.
/// Rejects with <c>cms.banner.archive_blocked_by_campaign_binding</c> when an
/// active <see cref="Entities.BannerCampaignBinding"/> exists; otherwise
/// transitions <c>live → archived</c> with the required reason note.
/// </summary>
public sealed class ArchiveBannerHandler
{
    private const int MinReasonNoteLength = 10;

    private readonly CmsDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly IPublisher _bus;
    private readonly TimeProvider _clock;

    public ArchiveBannerHandler(
        CmsDbContext db,
        IAuditEventPublisher audit,
        IPublisher bus,
        TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _bus = bus;
        _clock = clock;
    }

    public async Task<PublisherResult> HandleAsync(
        Guid bannerId,
        Guid actorId,
        string actorRole,
        ArchiveBannerRequest body,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.ArchiveReasonNote)
            || body.ArchiveReasonNote.Trim().Length < MinReasonNoteLength)
        {
            return PublisherResult.Reject(CmsReasonCode.ArchiveReasonNoteRequired,
                $"archive_reason_note must be at least {MinReasonNoteLength} characters.", 400);
        }

        var nowUtc = _clock.GetUtcNow();
        var row = await _db.BannerSlots.FirstOrDefaultAsync(b => b.Id == bannerId, ct);
        if (row is null)
        {
            return PublisherResult.Reject(CmsReasonCode.PreviewEntityNotFound, "Banner not found.", 404);
        }

        var sourceState = ContentLifecycleStateWire.FromWire(row.StateWire);
        if (sourceState != ContentLifecycleState.Live)
        {
            return PublisherResult.Reject(
                CmsReasonCode.ArchiveForbiddenInState(EntityKind.BannerSlot),
                $"Cannot archive a banner in state '{row.StateWire}'.", 405);
        }

        // Block archive when an active campaign binding exists (FR-023).
        var activeBinding = await _db.BannerCampaignBindings
            .Where(b => b.BannerId == bannerId && b.BindingStateWire == "active")
            .Select(b => new { b.Id, b.CampaignId })
            .FirstOrDefaultAsync(ct);
        if (activeBinding is not null)
        {
            return PublisherResult.Reject(
                CmsReasonCode.BannerArchiveBlockedByCampaignBinding,
                "Banner has an active campaign binding; unbind first.", 409,
                new Dictionary<string, object?>
                {
                    ["campaign_id"] = activeBinding.CampaignId,
                    ["binding_id"] = activeBinding.Id,
                });
        }

        // Compile-time + runtime guard.
        CmsContentLifecycle.AssertCanTransition(
            ContentLifecycleState.Live, ContentLifecycleState.Archived,
            EntityKind.BannerSlot, CmsTriggerKind.PublisherArchive, CmsActorKind.Publisher);

        row.StateWire = ContentLifecycleState.Archived.ToWire();
        row.ArchivedAtUtc = nowUtc;
        row.ArchiveReasonNote = body.ArchiveReasonNote.Trim();
        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: "cms.content.archived",
            EntityType: "cms.banner_slot",
            EntityId: row.Id,
            BeforeState: new { State = "live" },
            AfterState: new { State = "archived", ArchivedAtUtc = nowUtc },
            Reason: row.ArchiveReasonNote), ct);

        await _bus.Publish(new CmsBannerArchived(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: nowUtc,
            ActorId: actorId,
            EntityId: row.Id,
            VersionId: row.Id,
            MarketCode: row.MarketCode,
            ArchiveReasonNote: row.ArchiveReasonNote ?? string.Empty), ct);

        await _bus.Publish(new CmsCacheInvalidateBanner(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: nowUtc,
            EntityId: row.Id,
            VersionId: row.Id,
            MarketCode: row.MarketCode), ct);

        return PublisherResult.Ok(row);
    }
}
