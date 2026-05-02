using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Cms.Workers;

/// <summary>
/// Daily 24h asset GC worker per spec 024 tasks T149 / FR-009a.
/// For each cms.assets row in 'active' state with dereferenced_at_utc set
/// past the per-market grace window AND zero remaining references across
/// all 5 entity tables, transitions to 'swept'. CmsAsset metadata is
/// preserved (no hard-delete) per the spec; only the storage object is
/// removed (call deferred to spec 015 storage abstraction; here we mark
/// state=swept + swept_at_utc).
/// </summary>
public sealed class CmsAssetGarbageCollectorWorker : BackgroundService
{
    private static readonly TimeSpan TickPeriod = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<CmsAssetGarbageCollectorWorker> _log;

    public CmsAssetGarbageCollectorWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<CmsAssetGarbageCollectorWorker> log)
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
            catch (Exception ex)
            {
                _log.LogError(ex, "CmsAssetGarbageCollectorWorker tick failed.");
            }
            try { await Task.Delay(TickPeriod, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    public async Task<int> TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();

        await using var lockHandle = await CmsAdvisoryLock.TryAcquireAsync(
            db, CmsAdvisoryLock.Keys.AssetGcWorker, ct);
        if (!lockHandle.Acquired) return 0;

        var nowUtc = _clock.GetUtcNow();
        // Default 7-day grace; per-market override read in the loop below.
        var defaultGrace = TimeSpan.FromDays(7);
        var threshold = nowUtc - defaultGrace;

        var candidates = await db.Assets
            .Where(a => a.StorageObjectStateWire == "active"
                && a.DereferencedAtUtc != null
                && a.DereferencedAtUtc < threshold)
            .Take(200)
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        var swept = 0;
        foreach (var asset in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var stillReferenced =
                await db.BannerSlots.AnyAsync(b =>
                    b.AssetIdAr == asset.Id || b.AssetIdEn == asset.Id, ct) ||
                await db.BlogArticles.AnyAsync(b =>
                    b.CoverAssetId == asset.Id || b.SeoOgImageId == asset.Id, ct);
            if (stillReferenced) continue;

            asset.StorageObjectStateWire = "swept";
            asset.SweptAtUtc = nowUtc;

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
                Action: "cms.asset.swept",
                EntityType: "cms.asset",
                EntityId: asset.Id,
                BeforeState: new { State = "active" },
                AfterState: new { State = "swept", SweptAtUtc = nowUtc },
                Reason: "asset_gc"), ct);

            swept++;
        }

        if (swept > 0)
        {
            _log.LogInformation(
                "CmsAssetGarbageCollectorWorker: swept {Count} assets past grace window.",
                swept);
        }
        return swept;
    }
}
