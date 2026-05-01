using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BackendApi.Modules.Reviews.Workers;

/// <summary>
/// Spec 022 T133 — daily reconciliation worker. Recomputes every
/// <c>(product_id, market_code)</c> aggregate from scratch as a safety net
/// for missed events (process crashes, transactional rollbacks after the
/// immediate-on-transition publish, etc.). Idempotent — rebuilds overwrite
/// atomically via the same INSERT...ON CONFLICT DO UPDATE used inline.
///
/// <para>Advisory-locked so multiple replicas don't double-execute. When the
/// lock is held by another instance, this pass logs + exits cleanly.</para>
/// </summary>
public sealed class RatingAggregateRebuildWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ReviewsWorkerOptions> options,
    TimeProvider clock,
    ILogger<RatingAggregateRebuildWorker> logger) : BackgroundService
{
    public Task ExecuteOnceAsync(CancellationToken ct) => RunPassAsync(ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialDelay = options.Value.RatingAggregateRebuild.InitialDelay;
        if (initialDelay > TimeSpan.Zero)
        {
            try { await Task.Delay(initialDelay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }

        var period = options.Value.RatingAggregateRebuild.Period;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "RatingAggregateRebuildWorker pass failed; will retry next period.");
            }

            try { await Task.Delay(period, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunPassAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        var recomputer = scope.ServiceProvider.GetRequiredService<RatingAggregateRecomputer>();

        var dataSource = scope.ServiceProvider.GetService<NpgsqlDataSource>();
        await using var lockHandle = await ReviewsAdvisoryLock.TryAcquireAsync(
            db, ReviewsAdvisoryLock.Keys.RatingAggregateRebuild, ct, dataSource);
        if (!lockHandle.Acquired)
        {
            logger.LogInformation(
                "RatingAggregateRebuild advisory lock held by another instance; skipping pass.");
            return;
        }

        // Find every (product, market) tuple that has at least one
        // visible/flagged review. Aggregates that no longer have any
        // contributing review fall to ReviewCount=0 + AvgRating=null via the
        // recomputer's INSERT...ON CONFLICT DO UPDATE.
        var pairs = await db.Reviews.AsNoTracking()
            .Where(r => r.State == Primitives.ReviewState.Visible
                     || r.State == Primitives.ReviewState.Flagged)
            .Select(r => new { r.ProductId, r.MarketCode })
            .Distinct()
            .ToListAsync(ct);

        // Also touch any aggregate row whose contributors all left the counted
        // set since the last pass — those need to be reconciled to zero.
        var orphanAggregates = await db.RatingAggregates.AsNoTracking()
            .Where(a => a.ReviewCount > 0 &&
                !db.Reviews.Any(r =>
                    r.ProductId == a.ProductId
                    && r.MarketCode == a.MarketCode
                    && (r.State == Primitives.ReviewState.Visible
                     || r.State == Primitives.ReviewState.Flagged)))
            .Select(a => new { a.ProductId, a.MarketCode })
            .ToListAsync(ct);

        var touched = pairs.Concat(orphanAggregates)
            .Select(p => (p.ProductId, p.MarketCode))
            .Distinct()
            .ToList();

        var nowUtc = clock.GetUtcNow();
        foreach (var (productId, marketCode) in touched)
        {
            await recomputer.RecomputeAsync(productId, marketCode, ct);
        }

        logger.LogInformation(
            "RatingAggregateRebuild reconciliation pass complete at {NowUtc}: {Count} aggregate rows touched.",
            nowUtc, touched.Count);
    }
}
