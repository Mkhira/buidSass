using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using BackendApi.Modules.Pricing.Workers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Workers;

/// <summary>
/// Spec 007-b T138 — <see cref="BrokenReferenceAutoDeactivationWorker"/>
/// auto-deactivates active coupons whose every reference has been broken for
/// ≥ 7 days; rows still inside the grace window are left alone (research §R13).
/// </summary>
[Collection("pricing-fixture")]
public sealed class BrokenReferenceAutoDeactivationWorkerTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Tick_AutoDeactivates_CouponsBrokenForMoreThanSevenDays()
    {
        await factory.ResetDatabaseAsync();

        var now = DateTimeOffset.UtcNow;
        var brokenLongAgo = now.AddDays(-10); // > 7 days grace
        var couponId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            db.Coupons.Add(BuildBrokenCoupon(couponId, brokenLongAgo, now));
            await db.SaveChangesAsync();
        }

        var worker = new BrokenReferenceAutoDeactivationWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(now),
            NullLogger<BrokenReferenceAutoDeactivationWorker>.Instance);
        await worker.TickAsync(CancellationToken.None);

        await using var scope2 = factory.Services.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<PricingDbContext>();
        var c = await db2.Coupons.AsNoTracking().SingleAsync(c => c.Id == couponId);
        c.State.Should().Be(LifecycleState.Deactivated,
            "coupon with broken_at_utc > 7 days ago must be auto-deactivated");
        c.StateChangedReasonNote.Should().Be("auto_deactivated:broken_references");
        c.StateChangedByActorId.Should().Be(CommercialPermissions.SystemActorId);
    }

    [Fact]
    public async Task Tick_LeavesAlone_CouponsBrokenForLessThanSevenDays()
    {
        await factory.ResetDatabaseAsync();

        var now = DateTimeOffset.UtcNow;
        var brokenRecently = now.AddDays(-3); // < 7 days grace
        var couponId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            db.Coupons.Add(BuildBrokenCoupon(couponId, brokenRecently, now));
            await db.SaveChangesAsync();
        }

        var worker = new BrokenReferenceAutoDeactivationWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(now),
            NullLogger<BrokenReferenceAutoDeactivationWorker>.Instance);
        await worker.TickAsync(CancellationToken.None);

        await using var scope2 = factory.Services.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<PricingDbContext>();
        var c = await db2.Coupons.AsNoTracking().SingleAsync(c => c.Id == couponId);
        c.State.Should().Be(LifecycleState.Active,
            "coupon broken < 7 days ago remains active until the grace window elapses");
    }

    [Fact]
    public async Task Tick_IsIdempotent_NoSecondTransitionOnRerun()
    {
        await factory.ResetDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var brokenLongAgo = now.AddDays(-10);
        var couponId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            db.Coupons.Add(BuildBrokenCoupon(couponId, brokenLongAgo, now));
            await db.SaveChangesAsync();
        }

        var worker = new BrokenReferenceAutoDeactivationWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(now),
            NullLogger<BrokenReferenceAutoDeactivationWorker>.Instance);
        await worker.TickAsync(CancellationToken.None);
        await worker.TickAsync(CancellationToken.None);
        await worker.TickAsync(CancellationToken.None);

        await using var scope2 = factory.Services.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<PricingDbContext>();
        var deactivationAudits = await db2.CommercialAuditEvents.AsNoTracking()
            .Where(e => e.TargetEntityId == couponId &&
                        e.Kind == "coupon.lifecycle_transitioned")
            .CountAsync();
        deactivationAudits.Should().Be(1,
            "subsequent ticks must NOT add a second transition audit row");
    }

    private static Coupon BuildBrokenCoupon(Guid id, DateTimeOffset brokenAt, DateTimeOffset now) => new()
    {
        Id = id,
        Code = $"BREAK-{id.ToString("N")[..6]}",
        Kind = "percent",
        Value = 5,
        CapMinor = 1_00_000,
        PerCustomerLimit = 1,
        OverallLimit = 100,
        ExcludesRestricted = false,
        StacksWithPromotions = true,
        MarketCodes = new[] { "sa" },
        ValidFrom = now.AddDays(-30),
        ValidTo = now.AddDays(60),
        DisplayInBanners = false,
        LabelAr = "مرجع منكسر",
        LabelEn = "Broken-reference test",
        State = LifecycleState.Active,
        StateChangedAtUtc = now.AddDays(-30),
        StateChangedByActorId = CommercialPermissions.SystemActorId,
        AuthorActorId = CommercialPermissions.SystemActorId,
        IsActive = true,
        AppliesToBroken = true,
        AppliesToBrokenAtUtc = brokenAt,
        CreatedAt = now.AddDays(-30),
        UpdatedAt = brokenAt,
    };
}
