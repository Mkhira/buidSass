using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Pricing.Workers;

/// <summary>
/// Spec 007-b T148 (SC-004) — daily integrity scan. Observation-only worker
/// that scans <c>pricing.coupons</c>, <c>pricing.promotions</c> and
/// <c>pricing.campaigns</c> for any row in <c>active</c> with malformed
/// invariants (<c>valid_to ≤ valid_from</c> OR missing bilingual labels) and
/// writes findings to a structured log channel.
///
/// The worker writes NO rows; a non-zero count in the log channel triggers an
/// ops alert.
/// </summary>
public sealed class CommercialIntegrityScanWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<CommercialIntegrityScanWorker> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);
    private static readonly TimeOnly DailyRunUtc = new(3, 0);
    private static readonly TimeSpan DailyRunWindow = TimeSpan.FromMinutes(30);

    private DateOnly _lastRunDate = DateOnly.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = time.GetUtcNow();
                var today = DateOnly.FromDateTime(now.UtcDateTime);
                var nowTimeOnly = TimeOnly.FromDateTime(now.UtcDateTime);
                if (_lastRunDate != today &&
                    nowTimeOnly >= DailyRunUtc && nowTimeOnly < DailyRunUtc.Add(DailyRunWindow))
                {
                    await TickAsync(stoppingToken);
                    _lastRunDate = today;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "pricing.commercial-integrity-scan.cycle-failed");
            }

            try
            {
                await Task.Delay(TickInterval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task<int> TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();

        var violations = 0;

        // Coupons: state=active AND (valid_to <= valid_from OR label missing)
        var coupons = await db.Coupons.AsNoTracking()
            .Where(c => c.State == LifecycleState.Active && c.DeletedAt == null)
            .Where(c =>
                (c.ValidFrom != null && c.ValidTo != null && c.ValidTo <= c.ValidFrom) ||
                string.IsNullOrEmpty(c.LabelAr) ||
                string.IsNullOrEmpty(c.LabelEn))
            .Select(c => new { c.Id, c.Code, c.MarketCodes })
            .ToListAsync(ct);
        foreach (var c in coupons)
        {
            logger.LogWarning(
                "commercial.integrity.violation kind=coupon id={Id} code={Code} markets={Markets}",
                c.Id, c.Code, string.Join(",", c.MarketCodes));
            violations++;
        }

        // Promotions: same shape (active AND bad window OR missing labels).
        var promos = await db.Promotions.AsNoTracking()
            .Where(p => p.State == LifecycleState.Active && p.DeletedAt == null)
            .Where(p =>
                (p.StartsAt != null && p.EndsAt != null && p.EndsAt <= p.StartsAt) ||
                string.IsNullOrEmpty(p.LabelAr) ||
                string.IsNullOrEmpty(p.LabelEn))
            .Select(p => new { p.Id, p.Name, p.MarketCodes })
            .ToListAsync(ct);
        foreach (var p in promos)
        {
            logger.LogWarning(
                "commercial.integrity.violation kind=promotion id={Id} name={Name} markets={Markets}",
                p.Id, p.Name, string.Join(",", p.MarketCodes));
            violations++;
        }

        // Campaigns: state=active AND (valid_to <= valid_from OR bilingual name missing).
        var campaigns = await db.Campaigns.AsNoTracking()
            .Where(c => c.State == LifecycleState.Active)
            .Where(c =>
                c.ValidTo <= c.ValidFrom ||
                string.IsNullOrEmpty(c.NameAr) ||
                string.IsNullOrEmpty(c.NameEn))
            .Select(c => new { c.Id, c.NameEn, c.Markets })
            .ToListAsync(ct);
        foreach (var c in campaigns)
        {
            logger.LogWarning(
                "commercial.integrity.violation kind=campaign id={Id} name={Name} markets={Markets}",
                c.Id, c.NameEn, string.Join(",", c.Markets));
            violations++;
        }

        logger.LogInformation(
            "pricing.commercial-integrity-scan.tick violations={Count}", violations);
        return violations;
    }
}
