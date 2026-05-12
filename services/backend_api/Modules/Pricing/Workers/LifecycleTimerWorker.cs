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
        // CodeRabbit PR #81 round 1 Major: skip activation when the window has
        // already ended. Without the ValidTo guard, a row whose entire window
        // is now in the past would activate first and expire later in the same
        // tick, emitting a bogus Activated event on the way to Expired.
        var couponsToActivate = await db.Coupons
            .Where(c => c.State == LifecycleState.Scheduled &&
                        c.ValidFrom != null && c.ValidFrom <= nowUtc &&
                        (c.ValidTo == null || c.ValidTo > nowUtc) &&
                        c.DeletedAt == null)
            .Take(200)
            .ToListAsync(ct);
        foreach (var c in couponsToActivate)
        {
            await AdvanceCouponAsync(db, audit, events, c, LifecycleState.Active, nowUtc, ct, logger);
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
            await AdvanceCouponAsync(db, audit, events, c, LifecycleState.Expired, nowUtc, ct, logger);
        }

        // ---------- Promotions ----------
        // CodeRabbit PR #81 round 1 Major: same end-bound guard as coupons.
        var promosToActivate = await db.Promotions
            .Where(p => p.State == LifecycleState.Scheduled &&
                        p.StartsAt != null && p.StartsAt <= nowUtc &&
                        (p.EndsAt == null || p.EndsAt > nowUtc) &&
                        p.DeletedAt == null)
            .Take(200)
            .ToListAsync(ct);
        foreach (var p in promosToActivate)
        {
            await AdvancePromotionAsync(db, audit, events, p, LifecycleState.Active, nowUtc, ct, logger);
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
            await AdvancePromotionAsync(db, audit, events, p, LifecycleState.Expired, nowUtc, ct, logger);
        }

        // ---------- Campaigns ----------
        // CodeRabbit PR #81 round 1 Major: campaigns have non-nullable
        // ValidFrom/ValidTo (data-model §2.4), so the end-bound guard is a
        // direct comparison.
        var campaignsToActivate = await db.Campaigns
            .Where(c => c.State == LifecycleState.Scheduled &&
                        c.ValidFrom <= nowUtc &&
                        c.ValidTo > nowUtc)
            .Take(200)
            .ToListAsync(ct);
        foreach (var c in campaignsToActivate)
        {
            await AdvanceCampaignAsync(db, audit, c, LifecycleState.Active, nowUtc, ct, logger);
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
            await AdvanceCampaignAsync(db, audit, c, LifecycleState.Expired, nowUtc, ct, logger);
        }
    }

    private static async Task AdvanceCouponAsync(
        PricingDbContext db, CommercialAuditWriter audit, IPublisher events,
        Coupon c, LifecycleState target, DateTimeOffset nowUtc, CancellationToken ct,
        ILogger logger)
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

        // The local commercial_audit_events row is staged on the same change
        // tracker as the state mutation, so SaveChangesAsync commits both
        // atomically. The platform-channel audit publish + domain event
        // publish run after commit and are best-effort: a failure here would
        // leave the row in its new state without the downstream side-effect.
        // CodeRabbit PR #81 round 1 Major flagged this; we swallow + log so
        // one bad subscriber cannot strand the worker for the next tick
        // (where `State == target` short-circuits) — at-least-once domain
        // events would require an outbox, deferred to follow-up per the
        // research §R1 advisory-lock-only baseline.
        await db.SaveChangesAsync(ct);
        await SafePublishAsync(publish, ct, logger, "coupon.audit", c.Id);

        if (target == LifecycleState.Active)
        {
            await SafePublishAsync(token => events.Publish(new CouponActivated(
                c.Id, c.Code, c.MarketCodes, c.ValidFrom, c.ValidTo, nowUtc, SystemActorId), token),
                ct, logger, "coupon.activated", c.Id);
        }
        else if (target == LifecycleState.Expired)
        {
            await SafePublishAsync(token => events.Publish(new CouponExpired(c.Id, nowUtc), token),
                ct, logger, "coupon.expired", c.Id);
        }
    }

    private static async Task AdvancePromotionAsync(
        PricingDbContext db, CommercialAuditWriter audit, IPublisher events,
        Promotion p, LifecycleState target, DateTimeOffset nowUtc, CancellationToken ct,
        ILogger logger)
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
        await SafePublishAsync(publish, ct, logger, "promotion.audit", p.Id);

        if (target == LifecycleState.Active)
        {
            await SafePublishAsync(token => events.Publish(new PromotionActivated(
                p.Id, p.Name, p.MarketCodes, p.StartsAt, p.EndsAt, nowUtc, SystemActorId), token),
                ct, logger, "promotion.activated", p.Id);
        }
        else if (target == LifecycleState.Expired)
        {
            await SafePublishAsync(token => events.Publish(new PromotionExpired(p.Id, nowUtc), token),
                ct, logger, "promotion.expired", p.Id);
        }
    }

    private static async Task AdvanceCampaignAsync(
        PricingDbContext db, CommercialAuditWriter audit,
        Campaign c, LifecycleState target, DateTimeOffset nowUtc, CancellationToken ct,
        ILogger logger)
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
        await SafePublishAsync(publish, ct, logger, "campaign.audit", c.Id);
    }

    private static async Task SafePublishAsync(
        Func<CancellationToken, Task> publish, CancellationToken ct,
        ILogger logger, string channel, Guid entityId)
    {
        try
        {
            await publish(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "pricing.lifecycle-timer.publish-failed channel={Channel} entity={EntityId}",
                channel, entityId);
        }
    }
}
