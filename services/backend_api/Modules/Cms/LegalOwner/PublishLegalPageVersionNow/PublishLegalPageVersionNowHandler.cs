using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Cms.LegalOwner.PublishLegalPageVersionNow;

/// <summary>
/// POST /v1/admin/cms/legal-pages/{kind}/versions/{id}/publish-now per spec
/// 024 contract §5. Promotes <c>{draft|scheduled} → live</c> AND, atomically
/// in the same DB transaction, supersedes the prior <c>live</c> version of
/// the same <c>(kind, market)</c>. Concurrent publishes serialize behind the
/// per-(kind, market) advisory lock + the partial unique index; the loser
/// surfaces a typed
/// <see cref="CmsReasonCode.LegalPageVersionConflict"/> 409, never a 500.
/// Emits <c>cms.legal_page.version.published</c> +
/// <c>cms.legal_page.version.superseded</c> after commit.
/// </summary>
public sealed class PublishLegalPageVersionNowHandler
{
    private readonly CmsDbContext _db;
    private readonly LegalPageSupersessionTransaction _supersession;
    private readonly IAuditEventPublisher _audit;
    private readonly IPublisher _bus;
    private readonly TimeProvider _clock;
    private readonly ILogger<PublishLegalPageVersionNowHandler> _log;

    public PublishLegalPageVersionNowHandler(
        CmsDbContext db,
        LegalPageSupersessionTransaction supersession,
        IAuditEventPublisher audit,
        IPublisher bus,
        TimeProvider clock,
        ILogger<PublishLegalPageVersionNowHandler> log)
    {
        _db = db;
        _supersession = supersession;
        _audit = audit;
        _bus = bus;
        _clock = clock;
        _log = log;
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

        var sourceState = ContentLifecycleStateWire.FromWire(row.StateWire);
        if (sourceState != ContentLifecycleState.Draft && sourceState != ContentLifecycleState.Scheduled)
        {
            return LegalPagePublisherResult.Reject(
                CmsReasonCode.DraftNotEditable,
                "Only draft or scheduled legal page versions can be published now.", 400);
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

        // Detach so the supersession transaction owns the tracked instances.
        _db.ChangeTracker.Clear();

        LegalPageSupersessionTransaction.SupersessionOutcome outcome;
        try
        {
            outcome = await _supersession.ExecuteAsync(
                _db,
                row,
                nowUtc,
                isSuperAdmin ? CmsActorKind.SuperAdmin : CmsActorKind.LegalOwner,
                CmsTriggerKind.PublisherPublishNow,
                ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _log.LogWarning(ex,
                "PublishLegalPageVersionNow: concurrent publisher won; returning 409 for version {VersionId}.",
                versionId);
            return LegalPagePublisherResult.Reject(
                CmsReasonCode.LegalPageVersionConflict,
                "A concurrent publish raced this request; reload and retry.",
                409);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _log.LogWarning(ex,
                "PublishLegalPageVersionNow: optimistic concurrency conflict for version {VersionId}.",
                versionId);
            return LegalPagePublisherResult.Reject(
                CmsReasonCode.LegalPageVersionConflict,
                "Legal page version row was modified concurrently.",
                409);
        }

        // Audit + domain events post-commit.
        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: "cms.content.published",
            EntityType: "cms.legal_page_version",
            EntityId: outcome.NewLiveRow.Id,
            BeforeState: new { State = sourceState.ToWire() },
            AfterState: new
            {
                State = "live",
                outcome.NewLiveRow.LegalPageKindWire,
                outcome.NewLiveRow.MarketCode,
                outcome.NewLiveRow.EffectiveAtUtc,
                outcome.NewLiveRow.PublishedAtUtc,
            },
            Reason: null), ct);

        await _bus.Publish(new CmsLegalPageVersionPublished(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: nowUtc,
            ActorId: actorId,
            EntityId: outcome.NewLiveRow.Id,
            VersionId: outcome.NewLiveRow.Id,
            MarketCode: outcome.NewLiveRow.MarketCode,
            LegalPageKindWire: outcome.NewLiveRow.LegalPageKindWire,
            VersionLabel: outcome.NewLiveRow.VersionLabel,
            EffectiveAtUtc: outcome.NewLiveRow.EffectiveAtUtc ?? nowUtc), ct);

        await _bus.Publish(new CmsCacheInvalidateLegalPage(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: nowUtc,
            EntityId: outcome.NewLiveRow.Id,
            VersionId: outcome.NewLiveRow.Id,
            MarketCode: outcome.NewLiveRow.MarketCode,
            LegalPageKindWire: outcome.NewLiveRow.LegalPageKindWire), ct);

        if (outcome.SupersededRow is { } prior)
        {
            await _audit.PublishAsync(new AuditEvent(
                ActorId: actorId,
                ActorRole: actorRole,
                Action: "cms.legal_page.version.superseded",
                EntityType: "cms.legal_page_version",
                EntityId: prior.Id,
                BeforeState: new { State = "live" },
                AfterState: new
                {
                    State = "superseded",
                    SupersededByVersionId = outcome.NewLiveRow.Id,
                    prior.SupersededAtUtc,
                },
                Reason: null), ct);

            await _bus.Publish(new CmsLegalPageVersionSuperseded(
                EventId: Guid.NewGuid(),
                OccurredAtUtc: nowUtc,
                ActorId: actorId,
                EntityId: prior.Id,
                VersionId: prior.Id,
                MarketCode: prior.MarketCode,
                LegalPageKindWire: prior.LegalPageKindWire,
                SupersededByVersionId: outcome.NewLiveRow.Id), ct);
        }

        return LegalPagePublisherResult.Ok(outcome.NewLiveRow, outcome.SupersededRow);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is Npgsql.PostgresException pg && pg.SqlState == "23505")
                return true;
        }
        return false;
    }
}
