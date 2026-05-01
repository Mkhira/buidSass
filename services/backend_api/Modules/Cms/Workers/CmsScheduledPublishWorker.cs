using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Cms.Services;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Cms.Workers;

/// <summary>
/// 60s-cadence BackgroundService promoting scheduled banners to <c>live</c>
/// at <c>scheduled_start_utc</c> and live banners to <c>archived</c> at
/// <c>scheduled_end_utc</c> per spec 024 quickstart §1 + research §R12.
///
/// Idempotent per <c>(entity_kind, entity_id, target_state)</c> via the row's
/// xmin row_version + a precondition <c>WHERE State = 'scheduled'</c> on the
/// transition update; capacity-cap re-check before banner promotion (emits
/// <see cref="CmsBannerScheduledPublishBlockedCapacity"/> when blocked, leaves
/// the row in <c>scheduled</c>).
///
/// Coordination: session-scoped <c>pg_try_advisory_lock</c> on
/// <see cref="CmsAdvisoryLock.Keys.ScheduledPublishWorker"/>; runners that fail
/// to acquire the lock yield until the next tick.
/// </summary>
public sealed class CmsScheduledPublishWorker : BackgroundService
{
    private static readonly TimeSpan TickPeriod = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CapacityBlockedAlertWindow = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<CmsScheduledPublishWorker> _log;

    public CmsScheduledPublishWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<CmsScheduledPublishWorker> log)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger the first tick so concurrent replicas don't all race the
        // advisory lock at the same instant on a fresh deploy.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(0, 30)), stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "CmsScheduledPublishWorker tick failed; retrying after {Period}", TickPeriod);
            }

            try
            {
                await Task.Delay(TickPeriod, stoppingToken);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    public async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<CmsDbContext>();

        await using var lockHandle = await CmsAdvisoryLock.TryAcquireAsync(
            db, CmsAdvisoryLock.Keys.ScheduledPublishWorker, ct);
        if (!lockHandle.Acquired)
        {
            _log.LogDebug("CmsScheduledPublishWorker: advisory lock held by another replica; skipping tick.");
            return;
        }

        var nowUtc = _clock.GetUtcNow();
        await PromoteScheduledBannersToLiveAsync(sp, db, nowUtc, ct);
        await PromoteLiveBannersToArchivedAsync(sp, db, nowUtc, ct);
    }

    // ── scheduled → live (banners) ───────────────────────────────────────

    private async Task PromoteScheduledBannersToLiveAsync(
        IServiceProvider sp,
        CmsDbContext db,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var scheduledWire = ContentLifecycleState.Scheduled.ToWire();
        var liveWire = ContentLifecycleState.Live.ToWire();

        var due = await db.BannerSlots
            .Where(b => b.StateWire == scheduledWire)
            .Where(b => b.ScheduledStartUtc != null && b.ScheduledStartUtc <= nowUtc)
            .Where(b => b.ScheduledEndUtc == null || b.ScheduledEndUtc > nowUtc)
            .OrderBy(b => b.ScheduledStartUtc)
            .Take(100)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        var capacity = sp.GetRequiredService<BannerCapacityCalculator>();
        var ctaValidator = sp.GetRequiredService<BannerCtaValidator>();
        var audit = sp.GetRequiredService<IAuditEventPublisher>();
        var bus = sp.GetRequiredService<IPublisher>();

        foreach (var row in due)
        {
            ct.ThrowIfCancellationRequested();

            // Capacity re-check at promotion time.
            var policy = await ResolvePolicyAsync(db, row.MarketCode, ct);
            try
            {
                await capacity.AssertCanPublishAsync(
                    db.BannerSlots.AsQueryable(),
                    BannerSlotKindWire.FromWire(row.SlotKindWire),
                    row.MarketCode,
                    policy,
                    nowUtc,
                    ct);
            }
            catch (BannerSlotCapacityExceededException ex)
            {
                await EmitBlockedCapacityAsync(row, ex, bus, audit, db, nowUtc, ct);
                continue; // leave row in scheduled
            }

            // CTA re-validation at promotion time (hard fail).
            try
            {
                row.CtaHealthWire = (await ctaValidator.ValidateForPublishAsync(row, ct)).ToWire();
            }
            catch (BannerCtaTargetUnresolvableException ex)
            {
                _log.LogWarning(ex,
                    "CmsScheduledPublishWorker: banner {BannerId} CTA unresolvable at promotion; row remains scheduled.",
                    row.Id);
                continue;
            }

            // State-machine guard.
            CmsContentLifecycle.AssertCanTransition(
                ContentLifecycleState.Scheduled, ContentLifecycleState.Live,
                EntityKind.BannerSlot, CmsTriggerKind.WorkerPromoteToLive, CmsActorKind.System);

            row.StateWire = liveWire;
            row.PublishedAtUtc = nowUtc;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another worker / publisher race; safely re-enter on next tick.
                _log.LogDebug(
                    "CmsScheduledPublishWorker: concurrency conflict promoting banner {BannerId} — will retry.",
                    row.Id);
                db.ChangeTracker.Clear();
                continue;
            }

            await audit.PublishAsync(new AuditEvent(
                ActorId: Guid.Empty,
                ActorRole: "system",
                Action: "cms.content.published",
                EntityType: "cms.banner_slot",
                EntityId: row.Id,
                BeforeState: new { State = "scheduled" },
                AfterState: new { State = "live", PublishedAtUtc = nowUtc },
                Reason: "worker_promote_to_live"), ct);

            await bus.Publish(new CmsBannerPublished(
                EventId: Guid.NewGuid(),
                OccurredAtUtc: nowUtc,
                ActorId: null,
                EntityId: row.Id,
                VersionId: row.Id,
                MarketCode: row.MarketCode,
                SlotKindWire: row.SlotKindWire), ct);

            await bus.Publish(new CmsCacheInvalidateBanner(
                EventId: Guid.NewGuid(),
                OccurredAtUtc: nowUtc,
                EntityId: row.Id,
                VersionId: row.Id,
                MarketCode: row.MarketCode), ct);
        }
    }

    // ── live → archived (banners on scheduled_end_utc) ───────────────────

    private async Task PromoteLiveBannersToArchivedAsync(
        IServiceProvider sp,
        CmsDbContext db,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var liveWire = ContentLifecycleState.Live.ToWire();
        var archivedWire = ContentLifecycleState.Archived.ToWire();

        var due = await db.BannerSlots
            .Where(b => b.StateWire == liveWire)
            .Where(b => b.ScheduledEndUtc != null && b.ScheduledEndUtc <= nowUtc)
            .OrderBy(b => b.ScheduledEndUtc)
            .Take(100)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        var audit = sp.GetRequiredService<IAuditEventPublisher>();
        var bus = sp.GetRequiredService<IPublisher>();

        foreach (var row in due)
        {
            ct.ThrowIfCancellationRequested();

            CmsContentLifecycle.AssertCanTransition(
                ContentLifecycleState.Live, ContentLifecycleState.Archived,
                EntityKind.BannerSlot, CmsTriggerKind.WorkerPromoteToArchived, CmsActorKind.System);

            row.StateWire = archivedWire;
            row.ArchivedAtUtc = nowUtc;
            row.ArchiveReasonNote ??= "scheduled_end_utc reached";

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                continue;
            }

            await audit.PublishAsync(new AuditEvent(
                ActorId: Guid.Empty,
                ActorRole: "system",
                Action: "cms.content.archived",
                EntityType: "cms.banner_slot",
                EntityId: row.Id,
                BeforeState: new { State = "live" },
                AfterState: new { State = "archived", ArchivedAtUtc = nowUtc },
                Reason: "worker_promote_to_archived"), ct);

            await bus.Publish(new CmsBannerArchived(
                EventId: Guid.NewGuid(),
                OccurredAtUtc: nowUtc,
                ActorId: null,
                EntityId: row.Id,
                VersionId: row.Id,
                MarketCode: row.MarketCode,
                ArchiveReasonNote: row.ArchiveReasonNote ?? string.Empty), ct);

            await bus.Publish(new CmsCacheInvalidateBanner(
                EventId: Guid.NewGuid(),
                OccurredAtUtc: nowUtc,
                EntityId: row.Id,
                VersionId: row.Id,
                MarketCode: row.MarketCode), ct);
        }
    }

    private async Task EmitBlockedCapacityAsync(
        Entities.BannerSlot row,
        BannerSlotCapacityExceededException ex,
        IPublisher bus,
        IAuditEventPublisher audit,
        CmsDbContext db,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        // Rate-limit 1 alert per banner per hour via the row's
        // last_stale_alert_at_utc column reused for capacity-blocked alert
        // boundary in V1 (cheap, no extra column needed). Refresh only when
        // outside the window.
        if (row.LastStaleAlertAtUtc is DateTimeOffset prev
            && nowUtc - prev < CapacityBlockedAlertWindow)
        {
            return;
        }

        row.LastStaleAlertAtUtc = nowUtc;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
        }

        await audit.PublishAsync(new AuditEvent(
            ActorId: Guid.Empty,
            ActorRole: "system",
            Action: "cms.banner.scheduled_publish_blocked_capacity",
            EntityType: "cms.banner_slot",
            EntityId: row.Id,
            BeforeState: null,
            AfterState: new { ex.SlotKind, ex.MarketCode, ex.CurrentLiveCount, ex.Cap },
            Reason: ex.ReasonCode), ct);

        await bus.Publish(new CmsBannerScheduledPublishBlockedCapacity(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: nowUtc,
            EntityId: row.Id,
            VersionId: row.Id,
            MarketCode: row.MarketCode,
            SlotKindWire: row.SlotKindWire,
            CurrentLiveCount: ex.CurrentLiveCount,
            Cap: ex.Cap), ct);
    }

    private static async Task<CmsMarketPolicy> ResolvePolicyAsync(
        CmsDbContext db, string marketCode, CancellationToken ct)
    {
        var schema = await db.MarketSchemas.FirstOrDefaultAsync(s => s.MarketCode == marketCode, ct);
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
