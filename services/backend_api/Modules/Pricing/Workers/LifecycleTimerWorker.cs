using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
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
/// Spec 007-b T135 — lifecycle timer (research §R1). Ticks every 60 s, advances
/// every <c>scheduled</c> row whose <c>valid_from</c> is now in the past to
/// <c>active</c>, and every <c>scheduled|active|deactivated</c> row whose
/// <c>valid_to</c> is in the past to <c>expired</c>. Idempotent (no-op if the
/// state is already correct). Drift budget: ≤ 60 s (SC-005).
///
/// Uses <see cref="TimeProvider"/> so tests can advance via
/// <c>FakeTimeProvider</c>.
/// </summary>
public sealed class LifecycleTimerWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<LifecycleTimerWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private static readonly Guid SystemActorId = CommercialPermissions.SystemActorId;
    private const string SystemActorRole = "system";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "pricing.lifecycle-timer.cycle-failed");
            }

            try
            {
                await Task.Delay(Interval, time, stoppingToken);
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

        // ---------- Coupons ----------
        var couponsToActivate = await db.Coupons
            .Where(c => c.State == LifecycleState.Scheduled &&
                        c.ValidFrom != null && c.ValidFrom <= nowUtc &&
                        c.DeletedAt == null)
            .Take(200)
            .ToListAsync(ct);
        foreach (var c in couponsToActivate)
        {
            await AdvanceCouponAsync(db, audit, events, c, LifecycleState.Active, nowUtc, ct);
        }

        var couponsToExpire = await db.Coupons
            .Where(c => (c.State == LifecycleState.Scheduled ||
                         c.State == LifecycleState.Active ||
                         c.State == LifecycleState.Deactivated) &&
                        c.ValidTo != null && c.ValidTo <= nowUtc &&
                        c.DeletedAt == null)
            .Take(200)
            .ToListAsync(ct);
        foreach (var c in couponsToExpire)
        {
            await AdvanceCouponAsync(db, audit, events, c, LifecycleState.Expired, nowUtc, ct);
        }

        // ---------- Promotions ----------
        var promosToActivate = await db.Promotions
            .Where(p => p.State == LifecycleState.Scheduled &&
                        p.StartsAt != null && p.StartsAt <= nowUtc &&
                        p.DeletedAt == null)
            .Take(200)
            .ToListAsync(ct);
        foreach (var p in promosToActivate)
        {
            await AdvancePromotionAsync(db, audit, events, p, LifecycleState.Active, nowUtc, ct);
        }

        var promosToExpire = await db.Promotions
            .Where(p => (p.State == LifecycleState.Scheduled ||
                         p.State == LifecycleState.Active ||
                         p.State == LifecycleState.Deactivated) &&
                        p.EndsAt != null && p.EndsAt <= nowUtc &&
                        p.DeletedAt == null)
            .Take(200)
            .ToListAsync(ct);
        foreach (var p in promosToExpire)
        {
            await AdvancePromotionAsync(db, audit, events, p, LifecycleState.Expired, nowUtc, ct);
        }

        // ---------- Campaigns ----------
        var campaignsToActivate = await db.Campaigns
            .Where(c => c.State == LifecycleState.Scheduled && c.ValidFrom <= nowUtc)
            .Take(200)
            .ToListAsync(ct);
        foreach (var c in campaignsToActivate)
        {
            await AdvanceCampaignAsync(db, audit, c, LifecycleState.Active, nowUtc, ct);
        }

        var campaignsToExpire = await db.Campaigns
            .Where(c => (c.State == LifecycleState.Scheduled ||
                         c.State == LifecycleState.Active ||
                         c.State == LifecycleState.Deactivated) &&
                        c.ValidTo <= nowUtc)
            .Take(200)
            .ToListAsync(ct);
        foreach (var c in campaignsToExpire)
        {
            await AdvanceCampaignAsync(db, audit, c, LifecycleState.Expired, nowUtc, ct);
        }
    }

    private static async Task AdvanceCouponAsync(
        PricingDbContext db, CommercialAuditWriter audit, IPublisher events,
        Coupon c, LifecycleState target, DateTimeOffset nowUtc, CancellationToken ct)
    {
        if (c.State == target) return;
        var before = new { state = c.State.ToString().ToLowerInvariant() };
        c.State = target;
        c.StateChangedAtUtc = nowUtc;
        c.StateChangedByActorId = SystemActorId;
        c.IsActive = target == LifecycleState.Active;
        c.UpdatedAt = nowUtc;
        var after = new { state = c.State.ToString().ToLowerInvariant() };

        var publish = audit.StageLocal(
            "coupon", c.Id, "coupon.lifecycle_transitioned",
            SystemActorId, SystemActorRole,
            before, after, new { state_change = new { from = before.state, to = after.state }, via = "timer" },
            reasonNote: null, correlationId: null, nowUtc);

        await db.SaveChangesAsync(ct);
        await publish(ct);

        if (target == LifecycleState.Active)
        {
            await events.Publish(new CouponActivated(
                c.Id, c.Code, c.MarketCodes, c.ValidFrom, c.ValidTo, nowUtc, SystemActorId), ct);
        }
        else if (target == LifecycleState.Expired)
        {
            await events.Publish(new CouponExpired(c.Id, nowUtc), ct);
        }
    }

    private static async Task AdvancePromotionAsync(
        PricingDbContext db, CommercialAuditWriter audit, IPublisher events,
        Promotion p, LifecycleState target, DateTimeOffset nowUtc, CancellationToken ct)
    {
        if (p.State == target) return;
        var before = new { state = p.State.ToString().ToLowerInvariant() };
        p.State = target;
        p.StateChangedAtUtc = nowUtc;
        p.StateChangedByActorId = SystemActorId;
        p.IsActive = target == LifecycleState.Active;
        p.UpdatedAt = nowUtc;
        var after = new { state = p.State.ToString().ToLowerInvariant() };

        var publish = audit.StageLocal(
            "promotion", p.Id, "promotion.lifecycle_transitioned",
            SystemActorId, SystemActorRole,
            before, after, new { state_change = new { from = before.state, to = after.state }, via = "timer" },
            reasonNote: null, correlationId: null, nowUtc);

        await db.SaveChangesAsync(ct);
        await publish(ct);

        if (target == LifecycleState.Active)
        {
            await events.Publish(new PromotionActivated(
                p.Id, p.Name, p.MarketCodes, p.StartsAt, p.EndsAt, nowUtc, SystemActorId), ct);
        }
        else if (target == LifecycleState.Expired)
        {
            await events.Publish(new PromotionExpired(p.Id, nowUtc), ct);
        }
    }

    private static async Task AdvanceCampaignAsync(
        PricingDbContext db, CommercialAuditWriter audit,
        Campaign c, LifecycleState target, DateTimeOffset nowUtc, CancellationToken ct)
    {
        if (c.State == target) return;
        var before = new { state = c.State.ToString().ToLowerInvariant() };
        c.State = target;
        c.StateChangedAtUtc = nowUtc;
        c.StateChangedByActorId = SystemActorId;
        c.UpdatedAt = nowUtc;
        var after = new { state = c.State.ToString().ToLowerInvariant() };

        var publish = audit.StageLocal(
            "campaign", c.Id, "campaign.lifecycle_transitioned",
            SystemActorId, SystemActorRole,
            before, after, new { state_change = new { from = before.state, to = after.state }, via = "timer" },
            reasonNote: null, correlationId: null, nowUtc);

        await db.SaveChangesAsync(ct);
        await publish(ct);
    }
}
