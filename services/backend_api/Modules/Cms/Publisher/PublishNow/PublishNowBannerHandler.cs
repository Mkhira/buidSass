using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Cms.Services;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackendApi.Modules.Cms.Publisher.PublishNow;

/// <summary>
/// POST /v1/admin/cms/banner-slots/{id}/publish-now per spec 024 contract §4.1.
/// Same gates as <see cref="SchedulePublish.SchedulePublishBannerHandler"/>;
/// transitions to <c>live</c> immediately and emits
/// <c>cms.banner.published</c> + <c>cms.cache.invalidate.banner</c> after commit.
/// Accepts both <c>draft</c> and <c>scheduled</c> source states.
/// </summary>
public sealed class PublishNowBannerHandler
{
    private readonly CmsDbContext _db;
    private readonly BannerCapacityCalculator _capacity;
    private readonly BannerCtaValidator _ctaValidator;
    private readonly IAuditEventPublisher _audit;
    private readonly IPublisher _bus;
    private readonly TimeProvider _clock;

    public PublishNowBannerHandler(
        CmsDbContext db,
        BannerCapacityCalculator capacity,
        BannerCtaValidator ctaValidator,
        IAuditEventPublisher audit,
        IPublisher bus,
        TimeProvider clock)
    {
        _db = db;
        _capacity = capacity;
        _ctaValidator = ctaValidator;
        _audit = audit;
        _bus = bus;
        _clock = clock;
    }

    public async Task<PublisherResult> HandleAsync(
        Guid bannerId,
        Guid actorId,
        string actorRole,
        CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow();
        var row = await _db.BannerSlots.FirstOrDefaultAsync(b => b.Id == bannerId, ct);
        if (row is null)
        {
            return PublisherResult.Reject(CmsReasonCode.PreviewEntityNotFound, "Banner not found.", 404);
        }

        var sourceState = ContentLifecycleStateWire.FromWire(row.StateWire);
        if (sourceState != ContentLifecycleState.Draft && sourceState != ContentLifecycleState.Scheduled)
        {
            return PublisherResult.Reject(CmsReasonCode.DraftNotEditable,
                "Only draft or scheduled banners can be published now.", 400);
        }

        // 1) Locale-completeness.
        var localeCheck = LocaleCompletenessGate.CheckBanner(
            row.HeadlineAr, row.HeadlineEn, row.AssetIdAr, row.AssetIdEn);
        if (!localeCheck.IsAllowed)
        {
            return PublisherResult.Reject(localeCheck.ReasonCode!,
                "Banner is missing required bilingual fields.", 400,
                new Dictionary<string, object?> { ["missing_fields"] = localeCheck.MissingFields });
        }

        // 2) Banner CTA validation.
        try
        {
            await _ctaValidator.ValidateForPublishAsync(row, ct);
            row.CtaHealthWire = CtaHealth.Verified.ToWire();
        }
        catch (BannerCtaTargetUnresolvableException ex)
        {
            return PublisherResult.Reject(ex.ReasonCode, "Banner CTA target cannot be resolved.", 400,
                new Dictionary<string, object?>
                {
                    ["cta_kind"] = ex.CtaKind,
                    ["cta_target"] = ex.CtaTarget,
                });
        }

        // 3) Capacity check + commit, serialized per (slot_kind, market_code)
        //    via a transaction-scoped Postgres advisory lock per FR-021a +
        //    SC-010. Without this, concurrent publishers all observe the
        //    same pre-cap count, all decide to publish, and the slot
        //    overflows the cap. The lock auto-releases at COMMIT/ROLLBACK.
        var policy = await ResolvePolicyAsync(row.MarketCode, ct);
        IDbContextTransaction? tx = null;
        try
        {
            tx = await _db.Database.BeginTransactionAsync(ct);
            await AcquireSlotPublishLockAsync(row.SlotKindWire, row.MarketCode, ct);

            // Reload the row INSIDE the transaction after the advisory lock is
            // held. Between the initial fetch (line 51) and lock acquisition a
            // concurrent request may have flipped this row's state (e.g.
            // published it, archived it, moved it back to draft). Without
            // reloading we would publish from a stale snapshot — re-running
            // the lifecycle gate against an out-of-date state and skipping
            // the capacity check against the most recent state. Re-running
            // the mutable gates against the fresh row closes the TOCTOU
            // window per CR Round 1 feedback.
            await _db.Entry(row).ReloadAsync(ct);
            sourceState = ContentLifecycleStateWire.FromWire(row.StateWire);
            if (sourceState != ContentLifecycleState.Draft && sourceState != ContentLifecycleState.Scheduled)
            {
                await tx.RollbackAsync(ct);
                return PublisherResult.Reject(CmsReasonCode.DraftNotEditable,
                    "Only draft or scheduled banners can be published now.", 400);
            }

            // Re-apply the CTA validation result that was set at line 78
            // before the reload. ReloadAsync overwrites in-memory changes
            // with the DB snapshot, so without this the persisted row would
            // not reflect the verified CTA. Per CR Round 2 feedback.
            row.CtaHealthWire = CtaHealth.Verified.ToWire();

            try
            {
                await _capacity.AssertCanPublishAsync(
                    _db.BannerSlots.AsQueryable(),
                    BannerSlotKindWire.FromWire(row.SlotKindWire),
                    row.MarketCode,
                    policy,
                    nowUtc,
                    ct);
            }
            catch (BannerSlotCapacityExceededException ex)
            {
                await tx.RollbackAsync(ct);
                return PublisherResult.Reject(ex.ReasonCode, "Banner slot capacity exceeded.", 400,
                    new Dictionary<string, object?>
                    {
                        ["slot_kind"] = ex.SlotKind.ToWire(),
                        ["market_code"] = ex.MarketCode,
                        ["current_live_count"] = ex.CurrentLiveCount,
                        ["cap"] = ex.Cap,
                    });
            }

            // 4) Compile-time state-machine guard against the fresh row state.
            CmsContentLifecycle.AssertCanTransition(
                sourceState, ContentLifecycleState.Live,
                EntityKind.BannerSlot, CmsTriggerKind.PublisherPublishNow, CmsActorKind.Publisher);

            row.StateWire = ContentLifecycleState.Live.ToWire();
            row.PublishedAtUtc = nowUtc;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync();
            }
        }

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: "cms.content.published",
            EntityType: "cms.banner_slot",
            EntityId: row.Id,
            BeforeState: new { State = sourceState.ToWire() },
            AfterState: new { State = "live", PublishedAtUtc = nowUtc },
            Reason: null), ct);

        // 5) Domain events (after commit).
        await _bus.Publish(new CmsBannerPublished(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: nowUtc,
            ActorId: actorId,
            EntityId: row.Id,
            VersionId: row.Id,
            MarketCode: row.MarketCode,
            SlotKindWire: row.SlotKindWire), ct);

        await _bus.Publish(new CmsCacheInvalidateBanner(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: nowUtc,
            EntityId: row.Id,
            VersionId: row.Id,
            MarketCode: row.MarketCode), ct);

        return PublisherResult.Ok(row);
    }

    /// <summary>
    /// Acquires a transaction-scoped Postgres advisory lock keyed on the
    /// hashed <c>(slot_kind, market_code)</c> tuple, serializing concurrent
    /// publishers contending for the same slot. The lock auto-releases at
    /// transaction COMMIT/ROLLBACK — the caller MUST be inside a transaction.
    /// </summary>
    private async Task AcquireSlotPublishLockAsync(string slotKindWire, string marketCode, CancellationToken ct)
    {
        // Postgres pg_advisory_xact_lock takes a bigint; derive a stable key
        // from the slot+market tuple via hashtext (built-in, deterministic).
        var key = $"cms.banner.publish:{slotKindWire}:{marketCode}";
        var conn = _db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@k, 0))";
        var p = cmd.CreateParameter();
        p.ParameterName = "k";
        p.Value = key;
        cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<CmsMarketPolicy> ResolvePolicyAsync(string marketCode, CancellationToken ct)
    {
        var schema = await _db.MarketSchemas.FirstOrDefaultAsync(s => s.MarketCode == marketCode, ct);
        if (schema is null) return CmsMarketPolicy.Default(marketCode);
        return new CmsMarketPolicy(
            MarketCode: schema.MarketCode,
            BannerMaxLivePerSlot: schema.BannerMaxLivePerSlot,
            FeaturedSectionMaxReferences: schema.FeaturedSectionMaxReferences,
            PreviewTokenDefaultTtlHours: schema.PreviewTokenDefaultTtlHours,
            DraftStalenessAlertDays: schema.DraftStalenessAlertDays,
            AssetGracePeriodDays: schema.AssetGracePeriodDays);
    }
}
