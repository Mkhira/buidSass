using BackendApi.Modules.Reviews.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Reviews.Workers;

/// <summary>
/// Spec 022 T134 / SC-004 — daily scan that finds reviews still in
/// <c>visible</c>/<c>flagged</c> state but whose underlying order line has
/// been refunded. The worker logs each violation + emits a metric; it does
/// NOT auto-correct (that's the job of <c>RefundCompletedHandler</c> on the
/// inbound event path).
///
/// <para>The scan is implemented as a join from the reviews table to the
/// orders read-side; in production this calls into spec 011's data layer.
/// For PR-4, the worker exposes its scan via an injected
/// <see cref="IRefundedOrderLineLookup"/> abstraction so it can be tested
/// without an Orders module dependency.</para>
/// </summary>
public sealed class ReviewIntegrityScanWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ReviewsWorkerOptions> options,
    TimeProvider clock,
    ILogger<ReviewIntegrityScanWorker> logger) : BackgroundService
{
    public Task ExecuteOnceAsync(CancellationToken ct) => RunPassAsync(ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialDelay = options.Value.ReviewIntegrityScan.InitialDelay;
        if (initialDelay > TimeSpan.Zero)
        {
            try { await Task.Delay(initialDelay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }

        var period = options.Value.ReviewIntegrityScan.Period;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "ReviewIntegrityScanWorker pass failed; will retry next period.");
            }

            try { await Task.Delay(period, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    public sealed record IntegrityScanReport(int Violations, IReadOnlyList<Guid> ViolatingReviewIds);

    public async Task<IntegrityScanReport> ScanAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        var lookup = scope.ServiceProvider.GetRequiredService<IRefundedOrderLineLookup>();

        var liveReviews = await db.Reviews.AsNoTracking()
            .Where(r => r.State == Primitives.ReviewState.Visible
                     || r.State == Primitives.ReviewState.Flagged)
            .Select(r => new { r.Id, r.OrderLineId, r.MarketCode })
            .ToListAsync(ct);

        if (liveReviews.Count == 0)
        {
            return new IntegrityScanReport(0, Array.Empty<Guid>());
        }

        var orderLineIds = liveReviews.Select(r => r.OrderLineId).Distinct().ToList();
        var refundedSet = await lookup.GetRefundedOrderLineIdsAsync(orderLineIds, ct);

        var violations = liveReviews
            .Where(r => refundedSet.Contains(r.OrderLineId))
            .ToList();

        foreach (var v in violations)
        {
            logger.LogWarning(
                "reviews.integrity violation: review {ReviewId} in {MarketCode} is still live but order line {OrderLineId} is refunded.",
                v.Id, v.MarketCode, v.OrderLineId);
        }

        return new IntegrityScanReport(
            Violations: violations.Count,
            ViolatingReviewIds: violations.Select(v => v.Id).ToList());
    }

    private async Task RunPassAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();

        await using var lockHandle = await ReviewsAdvisoryLock.TryAcquireAsync(
            db, ReviewsAdvisoryLock.Keys.ReviewIntegrityScan, ct);
        if (!lockHandle.Acquired)
        {
            logger.LogInformation(
                "ReviewIntegrityScan advisory lock held by another instance; skipping pass.");
            return;
        }

        var report = await ScanAsync(ct);
        logger.LogInformation(
            "ReviewIntegrityScan pass complete at {NowUtc}: {Violations} violations found.",
            clock.GetUtcNow(), report.Violations);
        // SC-004 metric — emitted as a structured log event so downstream metric
        // pipelines (Grafana / OpenTelemetry meter) can scrape from log ingestion
        // without coupling this worker to a specific metrics framework.
    }
}

/// <summary>
/// Read-side query used by the integrity scan to determine which order lines
/// have been refunded. Spec 013 (returns / refunds) supplies the production
/// binding. PR-4 ships a Null fallback that always reports "none refunded"
/// so the scanner is a no-op against an empty orders implementation.
/// </summary>
public interface IRefundedOrderLineLookup
{
    Task<IReadOnlySet<Guid>> GetRefundedOrderLineIdsAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken ct);
}

/// <summary>Conservative fallback — reports zero refunded order lines.</summary>
public sealed class NullRefundedOrderLineLookup : IRefundedOrderLineLookup
{
    public Task<IReadOnlySet<Guid>> GetRefundedOrderLineIdsAsync(
        IReadOnlyCollection<Guid> orderLineIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
}
