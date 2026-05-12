using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Pricing.Workers;

/// <summary>
/// Spec 007-b T136 / research §R13 — runs daily, finds Coupons / Promotions
/// whose <c>applies_to_broken_at_utc</c> is ≥ 7 days in the past AND whose
/// <c>state == active</c>, and auto-deactivates them with
/// <c>actor_id='system'</c> and reason_note=<c>auto_deactivated:broken_references</c>.
///
/// Eligibility condition (FR-034c): the row is auto-deactivated only when
/// EVERY reference is broken. The current implementation simplifies to the
/// stronger condition "<c>applies_to_broken=true</c> for ≥ 7 days" — when
/// upstream catalog churn produces a partially-broken rule, the queue-indicator
/// alone surfaces it for operator review (research §R15 day-0 alert).
///
/// Idempotent: ticks twice in the same UTC day produce no second transition.
/// </summary>
public sealed class BrokenReferenceAutoDeactivationWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<BrokenReferenceAutoDeactivationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan GraceWindow = TimeSpan.FromDays(7);
    private static readonly TimeOnly DailyRunUtc = new(2, 0);
    private static readonly Guid SystemActorId = CommercialPermissions.SystemActorId;
    private const string SystemActorRole = "system";
    private const string AutoDeactivationReasonNote = "auto_deactivated:broken_references";
    private static readonly TimeSpan DailyRunWindow = TimeSpan.FromMinutes(30);

    private DateOnly _lastRunDate = DateOnly.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = time.GetUtcNow();
                // Run if we're inside the daily window AND have not yet run today.
                var today = DateOnly.FromDateTime(now.UtcDateTime);
                var nowTimeOnly = TimeOnly.FromDateTime(now.UtcDateTime);
                var fromTime = DailyRunUtc;
                var toTime = DailyRunUtc.Add(DailyRunWindow);
                if (_lastRunDate != today &&
                    nowTimeOnly >= fromTime && nowTimeOnly < toTime)
                {
                    await TickAsync(stoppingToken);
                    _lastRunDate = today;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "pricing.broken-ref-auto-deactivation.cycle-failed");
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

    internal async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<CommercialAuditWriter>();
        var events = scope.ServiceProvider.GetRequiredService<IPublisher>();

        var nowUtc = time.GetUtcNow();
        var cutoff = nowUtc - GraceWindow;

        // Resolve per-market grace seconds for the deactivation event payload.
        var thresholdByMarket = await db.CommercialThresholds.AsNoTracking()
            .ToDictionaryAsync(t => t.MarketCode, ct);

        // --- Coupons ---
        var coupons = await db.Coupons
            .Where(c => c.State == LifecycleState.Active &&
                        c.AppliesToBroken &&
                        c.AppliesToBrokenAtUtc != null &&
                        c.AppliesToBrokenAtUtc <= cutoff &&
                        c.DeletedAt == null)
            .Take(500)
            .ToListAsync(ct);
        foreach (var c in coupons)
        {
            var before = new { state = c.State.ToString().ToLowerInvariant() };
            c.State = LifecycleState.Deactivated;
            c.StateChangedAtUtc = nowUtc;
            c.StateChangedByActorId = SystemActorId;
            c.StateChangedReasonNote = AutoDeactivationReasonNote;
            c.IsActive = false;
            c.UpdatedAt = nowUtc;
            var after = new { state = c.State.ToString().ToLowerInvariant() };

            var publish = audit.StageLocal(
                "coupon", c.Id, "coupon.lifecycle_transitioned",
                SystemActorId, SystemActorRole,
                before, after,
                new { state_change = new { from = before.state, to = after.state }, via = "auto_deactivation" },
                reasonNote: AutoDeactivationReasonNote, correlationId: null, nowUtc);

            await db.SaveChangesAsync(ct);
            // Post-commit publishes are best-effort — log + continue rather
            // than throw, so one bad subscriber cannot strand the worker for
            // the next tick (CodeRabbit PR #81 round 1 Major).
            await SafePublishAsync(publish, ct, "coupon.audit", c.Id);

            var grace = ResolveCouponGraceSeconds(c.MarketCodes, thresholdByMarket);
            await SafePublishAsync(token => events.Publish(new CouponDeactivated(
                c.Id, nowUtc, SystemActorId, AutoDeactivationReasonNote, grace), token),
                ct, "coupon.deactivated", c.Id);
        }

        // --- Promotions ---
        var promos = await db.Promotions
            .Where(p => p.State == LifecycleState.Active &&
                        p.AppliesToBroken &&
                        p.AppliesToBrokenAtUtc != null &&
                        p.AppliesToBrokenAtUtc <= cutoff &&
                        p.DeletedAt == null)
            .Take(500)
            .ToListAsync(ct);
        foreach (var p in promos)
        {
            var before = new { state = p.State.ToString().ToLowerInvariant() };
            p.State = LifecycleState.Deactivated;
            p.StateChangedAtUtc = nowUtc;
            p.StateChangedByActorId = SystemActorId;
            p.StateChangedReasonNote = AutoDeactivationReasonNote;
            p.IsActive = false;
            p.UpdatedAt = nowUtc;
            var after = new { state = p.State.ToString().ToLowerInvariant() };

            var publish = audit.StageLocal(
                "promotion", p.Id, "promotion.lifecycle_transitioned",
                SystemActorId, SystemActorRole,
                before, after,
                new { state_change = new { from = before.state, to = after.state }, via = "auto_deactivation" },
                reasonNote: AutoDeactivationReasonNote, correlationId: null, nowUtc);

            await db.SaveChangesAsync(ct);
            await SafePublishAsync(publish, ct, "promotion.audit", p.Id);

            var grace = ResolvePromotionGraceSeconds(p.MarketCodes, thresholdByMarket);
            await SafePublishAsync(token => events.Publish(new PromotionDeactivated(
                p.Id, nowUtc, SystemActorId, AutoDeactivationReasonNote, grace), token),
                ct, "promotion.deactivated", p.Id);
        }

        // --- Business pricing rows (company-link broken ≥ 7 days) ---
        var tierRows = await db.ProductTierPrices
            .Where(r => r.State == BusinessPricingState.Active &&
                        r.CompanyLinkBroken &&
                        r.CompanyLinkBrokenAtUtc != null &&
                        r.CompanyLinkBrokenAtUtc <= cutoff)
            .Take(500)
            .ToListAsync(ct);
        foreach (var r in tierRows)
        {
            var before = new { state = r.State.ToString().ToLowerInvariant() };
            r.State = BusinessPricingState.Deactivated;
            r.StateChangedAtUtc = nowUtc;
            r.StateChangedByActorId = SystemActorId;
            r.StateChangedReasonNote = AutoDeactivationReasonNote;
            r.UpdatedAt = nowUtc;
            var after = new { state = r.State.ToString().ToLowerInvariant() };

            var publish = audit.StageLocal(
                "business_pricing", r.Id, "business_pricing.row_changed",
                SystemActorId, SystemActorRole,
                before, after,
                new { state_change = new { from = before.state, to = after.state }, via = "auto_deactivation" },
                reasonNote: AutoDeactivationReasonNote, correlationId: null, nowUtc);

            await db.SaveChangesAsync(ct);
            await SafePublishAsync(publish, ct, "business_pricing.audit", r.Id);
        }

        if (coupons.Count + promos.Count + tierRows.Count > 0)
        {
            logger.LogInformation(
                "pricing.broken-ref-auto-deactivation.tick coupons={C} promotions={P} business_pricing={B}",
                coupons.Count, promos.Count, tierRows.Count);
        }
    }

    private async Task SafePublishAsync(
        Func<CancellationToken, Task> publish, CancellationToken ct, string channel, Guid entityId)
    {
        try
        {
            await publish(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "pricing.broken-ref-auto-deactivation.publish-failed channel={Channel} entity={EntityId}",
                channel, entityId);
        }
    }

    private static int ResolveCouponGraceSeconds(
        string[] marketCodes, IReadOnlyDictionary<string, Entities.CommercialThreshold> thresholds)
    {
        var grace = 1800;
        foreach (var mc in marketCodes)
        {
            var key = NormaliseMarket(mc);
            if (key is null) continue;
            if (thresholds.TryGetValue(key, out var row))
            {
                grace = Math.Min(grace, row.CouponInFlightGraceSeconds);
            }
        }
        return grace;
    }

    private static int ResolvePromotionGraceSeconds(
        string[] marketCodes, IReadOnlyDictionary<string, Entities.CommercialThreshold> thresholds)
    {
        var grace = 1800;
        foreach (var mc in marketCodes)
        {
            var key = NormaliseMarket(mc);
            if (key is null) continue;
            if (thresholds.TryGetValue(key, out var row))
            {
                grace = Math.Min(grace, row.PromotionInFlightGraceSeconds);
            }
        }
        return grace;
    }

    private static string? NormaliseMarket(string m) => m?.Trim().ToUpperInvariant() switch
    {
        "SA" or "KSA" => "SA",
        "EG" => "EG",
        _ => null,
    };
}
