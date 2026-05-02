using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.LegalOwner.SchedulePublishLegalPageVersion;

/// <summary>
/// POST /v1/admin/cms/legal-pages/{kind}/versions/{id}/schedule-publish per
/// spec 024 contract §5. Validates locale completeness + effective_at_utc
/// presence; transitions <c>draft → scheduled</c>; emits
/// <c>cms.content.scheduled</c> audit. The actual <c>scheduled → live</c>
/// promotion + prior-live supersession runs in <c>CmsScheduledPublishWorker</c>.
/// </summary>
public sealed class SchedulePublishLegalPageVersionHandler
{
    private readonly CmsDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public SchedulePublishLegalPageVersionHandler(
        CmsDbContext db,
        IAuditEventPublisher audit,
        TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<LegalPagePublisherResult> HandleAsync(
        Guid versionId,
        string legalPageKindWire,
        Guid actorId,
        string actorRole,
        bool isSuperAdmin,
        CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow();
        var row = await _db.LegalPageVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (row is null)
        {
            return LegalPagePublisherResult.Reject(
                CmsReasonCode.PreviewEntityNotFound, "Legal page version not found.", 404);
        }
        if (row.LegalPageKindWire != legalPageKindWire)
        {
            return LegalPagePublisherResult.Reject(
                CmsReasonCode.LegalPageNotFoundForMarket,
                "Legal page version kind mismatch.", 404);
        }
        if (row.StateWire != ContentLifecycleState.Draft.ToWire())
        {
            return LegalPagePublisherResult.Reject(
                CmsReasonCode.DraftNotEditable,
                "Only draft legal page versions can be scheduled.", 400);
        }

        // *-scoped publish requires super_admin.
        if (row.MarketCode == "*" && !isSuperAdmin)
        {
            return LegalPagePublisherResult.Reject(
                CmsReasonCode.LegalPageCrossMarketRequiresSuperAdmin,
                "Cross-market (*) legal page publish requires super_admin.", 403);
        }

        // Locale completeness + effective_at_utc presence.
        var gate = LocaleCompletenessGate.CheckLegalPageVersion(
            row.BodyAr, row.BodyEn, row.EffectiveAtUtc);
        if (!gate.IsAllowed)
        {
            var reason = gate.MissingFields.Contains("effective_at_utc")
                ? CmsReasonCode.PublishEffectiveAtRequired
                : CmsReasonCode.PublishLocaleCompletenessMissing;
            return LegalPagePublisherResult.Reject(
                reason,
                "Legal page version is missing required fields.",
                400,
                new Dictionary<string, object?> { ["missing_fields"] = gate.MissingFields });
        }

        CmsContentLifecycle.AssertCanTransition(
            ContentLifecycleState.Draft, ContentLifecycleState.Scheduled,
            EntityKind.LegalPageVersion,
            CmsTriggerKind.PublisherSchedule,
            isSuperAdmin ? CmsActorKind.SuperAdmin : CmsActorKind.LegalOwner);

        row.StateWire = ContentLifecycleState.Scheduled.ToWire();
        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: "cms.content.scheduled",
            EntityType: "cms.legal_page_version",
            EntityId: row.Id,
            BeforeState: new { State = "draft" },
            AfterState: new
            {
                State = "scheduled",
                row.LegalPageKindWire,
                row.MarketCode,
                row.EffectiveAtUtc,
                row.VersionLabel,
            },
            Reason: null), ct);

        return LegalPagePublisherResult.Ok(row);
    }
}
