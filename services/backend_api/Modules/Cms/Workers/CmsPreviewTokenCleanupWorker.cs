using BackendApi.Modules.Cms.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Cms.Workers;

/// <summary>
/// Daily cleanup worker that hard-deletes preview-token rows whose
/// <c>expires_at_utc + 30 days</c> are in the past. This is the ONLY hard-
/// delete path for preview tokens (FR-016). Per spec 024 quickstart §4 the
/// cadence is 04:00 UTC; we approximate with a 24h tick + a session-scoped
/// advisory lock so two replicas don't double-delete.
/// </summary>
public sealed class CmsPreviewTokenCleanupWorker : BackgroundService
{
    private static readonly TimeSpan TickPeriod = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan KeepAfterExpiry = TimeSpan.FromDays(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<CmsPreviewTokenCleanupWorker> _log;

    public CmsPreviewTokenCleanupWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<CmsPreviewTokenCleanupWorker> log)
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
                _log.LogError(ex, "CmsPreviewTokenCleanupWorker tick failed.");
            }

            try { await Task.Delay(TickPeriod, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    public async Task<int> TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        await using var lockHandle = await CmsAdvisoryLock.TryAcquireAsync(
            db, CmsAdvisoryLock.Keys.PreviewTokenCleanupWorker, ct);
        if (!lockHandle.Acquired)
        {
            return 0;
        }

        var nowUtc = _clock.GetUtcNow();
        var threshold = nowUtc - KeepAfterExpiry;

        var deleted = await db.PreviewTokens
            .Where(t => t.ExpiresAtUtc < threshold)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
        {
            _log.LogInformation(
                "CmsPreviewTokenCleanupWorker: hard-deleted {Count} preview-token rows expired before {Threshold}.",
                deleted, threshold);
        }
        return deleted;
    }
}
