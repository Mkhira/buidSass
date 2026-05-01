using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Cms.Workers;

/// <summary>
/// Daily 24h stale-draft alerter per spec 024 tasks T150 / FR-034a.
/// Flags drafts whose editor_save_at_utc is older than the per-market
/// draft_staleness_alert_days. Soft alert only — NEVER auto-archives.
/// Idempotent on (draft_id, alert_window_start_utc) via the row's
/// last_stale_alert_at_utc column.
/// </summary>
public sealed class CmsStaleDraftAlertWorker : BackgroundService
{
    private static readonly TimeSpan TickPeriod = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<CmsStaleDraftAlertWorker> _log;

    public CmsStaleDraftAlertWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<CmsStaleDraftAlertWorker> log)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { _log.LogError(ex, "CmsStaleDraftAlertWorker tick failed."); }
            try { await Task.Delay(TickPeriod, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    public async Task<int> TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
        var bus = scope.ServiceProvider.GetRequiredService<IPublisher>();

        await using var lockHandle = await CmsAdvisoryLock.TryAcquireAsync(
            db, CmsAdvisoryLock.Keys.StaleDraftAlertWorker, ct);
        if (!lockHandle.Acquired) return 0;

        var nowUtc = _clock.GetUtcNow();
        var schemas = await db.MarketSchemas.AsNoTracking().ToListAsync(ct);
        var defaultDays = 30;

        int alerts = 0;
        // Banners.
        alerts += await ScanAndAlertAsync(db, audit, bus, nowUtc, "banner_slot",
            db.BannerSlots.Where(r => r.StateWire == "draft"),
            r => r.EditorSaveAtUtc, r => r.LastStaleAlertAtUtc,
            r => r.OwnerActorId, r => r.MarketCode, r => r.Id,
            (r, t) => r.LastStaleAlertAtUtc = t,
            schemas, defaultDays, ct);
        // FeaturedSections.
        alerts += await ScanAndAlertAsync(db, audit, bus, nowUtc, "featured_section",
            db.FeaturedSections.Where(r => r.StateWire == "draft"),
            r => r.EditorSaveAtUtc, r => r.LastStaleAlertAtUtc,
            r => r.OwnerActorId, r => r.MarketCode, r => r.Id,
            (r, t) => r.LastStaleAlertAtUtc = t,
            schemas, defaultDays, ct);
        // FAQ.
        alerts += await ScanAndAlertAsync(db, audit, bus, nowUtc, "faq_entry",
            db.FaqEntries.Where(r => r.StateWire == "draft"),
            r => r.EditorSaveAtUtc, r => r.LastStaleAlertAtUtc,
            r => r.OwnerActorId, r => r.MarketCode, r => r.Id,
            (r, t) => r.LastStaleAlertAtUtc = t,
            schemas, defaultDays, ct);
        // Blog.
        alerts += await ScanAndAlertAsync(db, audit, bus, nowUtc, "blog_article",
            db.BlogArticles.Where(r => r.StateWire == "draft"),
            r => r.EditorSaveAtUtc, r => r.LastStaleAlertAtUtc,
            r => r.OwnerActorId, r => r.MarketCode, r => r.Id,
            (r, t) => r.LastStaleAlertAtUtc = t,
            schemas, defaultDays, ct);
        // Legal.
        alerts += await ScanAndAlertAsync(db, audit, bus, nowUtc, "legal_page_version",
            db.LegalPageVersions.Where(r => r.StateWire == "draft"),
            r => r.EditorSaveAtUtc, r => r.LastStaleAlertAtUtc,
            r => r.OwnerActorId, r => r.MarketCode, r => r.Id,
            (r, t) => r.LastStaleAlertAtUtc = t,
            schemas, defaultDays, ct);

        if (alerts > 0)
        {
            _log.LogInformation("CmsStaleDraftAlertWorker: emitted {Count} stale-draft alerts.", alerts);
        }
        return alerts;
    }

    private static async Task<int> ScanAndAlertAsync<T>(
        CmsDbContext db,
        IAuditEventPublisher audit,
        IPublisher bus,
        DateTimeOffset nowUtc,
        string entityKindWire,
        IQueryable<T> source,
        Func<T, DateTimeOffset> editorSave,
        Func<T, DateTimeOffset?> lastAlert,
        Func<T, Guid> ownerId,
        Func<T, string> marketCode,
        Func<T, Guid> rowId,
        Action<T, DateTimeOffset> stampLastAlert,
        IReadOnlyList<Entities.CmsMarketSchema> schemas,
        int defaultDays,
        CancellationToken ct) where T : class
    {
        var rows = await source.Take(500).ToListAsync(ct);
        if (rows.Count == 0) return 0;

        int count = 0;
        foreach (var row in rows)
        {
            var market = marketCode(row);
            var graceDays = schemas.FirstOrDefault(s => s.MarketCode == market)?.DraftStalenessAlertDays
                            ?? defaultDays;
            var threshold = nowUtc - TimeSpan.FromDays(graceDays);
            if (editorSave(row) >= threshold) continue;

            // Suppress repeats within one stale window.
            if (lastAlert(row) is DateTimeOffset prev && nowUtc - prev < TimeSpan.FromDays(graceDays / 4.0))
            {
                continue;
            }

            stampLastAlert(row, nowUtc);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); continue; }

            await audit.PublishAsync(new AuditEvent(
                ActorId: Guid.Empty,
                ActorRole: "system",
                Action: "cms.draft.stale_alert",
                EntityType: $"cms.{entityKindWire}",
                EntityId: rowId(row),
                BeforeState: null,
                AfterState: new
                {
                    EntityKindWire = entityKindWire,
                    OwnerActorId = ownerId(row),
                    EditorSaveAtUtc = editorSave(row),
                    AlertedAtUtc = nowUtc,
                },
                Reason: "draft_stale"), ct);

            await bus.Publish(new CmsDraftStaleAlert(
                EventId: Guid.NewGuid(),
                OccurredAtUtc: nowUtc,
                EntityId: rowId(row),
                EntityKindWire: entityKindWire,
                OwnerActorId: ownerId(row),
                MarketCode: market,
                DraftCreatedAtUtc: editorSave(row)), ct);

            count++;
        }
        return count;
    }
}
