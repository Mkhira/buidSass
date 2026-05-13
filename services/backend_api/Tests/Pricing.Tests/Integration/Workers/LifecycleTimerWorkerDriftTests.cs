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
/// Spec 007-b T137 — drift-budget test for <see cref="LifecycleTimerWorker"/>.
/// Schedules a batch of coupons with <c>valid_from</c> in the near future,
/// advances <see cref="FakeTimeProvider"/> past the boundary, runs one tick,
/// and asserts every row is now <c>active</c>. SC-005 caps drift at 60 s.
/// </summary>
[Collection("pricing-fixture")]
public sealed class LifecycleTimerWorkerDriftTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Tick_ActivatesScheduledCoupons_WhoseValidFromHasPassed()
    {
        await factory.ResetDatabaseAsync();

        var baseTime = DateTimeOffset.UtcNow;
        var scheduledFor = baseTime.AddSeconds(30);

        // Seed 20 scheduled coupons all with valid_from = baseTime + 30s.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            for (int i = 0; i < 20; i++)
            {
                db.Coupons.Add(new Coupon
                {
                    Id = Guid.NewGuid(),
                    Code = $"TIMER-{i:D2}",
                    Kind = "percent",
                    Value = 5,
                    CapMinor = 1_00_000,
                    PerCustomerLimit = 1,
                    OverallLimit = 100,
                    ExcludesRestricted = false,
                    StacksWithPromotions = true,
                    MarketCodes = new[] { "sa" },
                    ValidFrom = scheduledFor,
                    ValidTo = scheduledFor.AddDays(7),
                    DisplayInBanners = false,
                    LabelAr = "اختبار المؤقت",
                    LabelEn = "Timer worker test",
                    State = LifecycleState.Scheduled,
                    StateChangedAtUtc = baseTime,
                    StateChangedByActorId = CommercialPermissions.SystemActorId,
                    AuthorActorId = CommercialPermissions.SystemActorId,
                    IsActive = false,
                    CreatedAt = baseTime,
                    UpdatedAt = baseTime,
                });
            }
            await db.SaveChangesAsync();
        }

        // Advance fake-time past the boundary and tick once.
        var fakeTime = new FakeTimeProvider(scheduledFor.AddSeconds(15));
        var worker = new LifecycleTimerWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            fakeTime,
            NullLogger<LifecycleTimerWorker>.Instance);
        await worker.TickAsync(CancellationToken.None);

        // All 20 rows should now be active.
        await using var scopeAssert = factory.Services.CreateAsyncScope();
        var dbA = scopeAssert.ServiceProvider.GetRequiredService<PricingDbContext>();
        var activatedCount = await dbA.Coupons.AsNoTracking()
            .CountAsync(c => c.Code.StartsWith("TIMER-") &&
                              c.State == LifecycleState.Active);
        activatedCount.Should().Be(20,
            "every scheduled coupon whose valid_from is in the past must activate within one tick");
    }

    [Fact]
    public async Task Tick_ExpiresCoupons_WhoseValidToHasPassed()
    {
        await factory.ResetDatabaseAsync();
        var baseTime = DateTimeOffset.UtcNow;
        var couponId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            db.Coupons.Add(new Coupon
            {
                Id = couponId,
                Code = "TIMER-EXPIRE",
                Kind = "percent",
                Value = 5,
                CapMinor = 1_00_000,
                PerCustomerLimit = 1,
                OverallLimit = 100,
                ExcludesRestricted = false,
                StacksWithPromotions = true,
                MarketCodes = new[] { "sa" },
                ValidFrom = baseTime.AddHours(-2),
                ValidTo = baseTime.AddHours(-1), // already past
                DisplayInBanners = false,
                LabelAr = "انتهاء صلاحية",
                LabelEn = "Timer expire test",
                State = LifecycleState.Active,
                StateChangedAtUtc = baseTime.AddHours(-2),
                StateChangedByActorId = CommercialPermissions.SystemActorId,
                AuthorActorId = CommercialPermissions.SystemActorId,
                IsActive = true,
                CreatedAt = baseTime.AddDays(-1),
                UpdatedAt = baseTime.AddHours(-2),
            });
            await db.SaveChangesAsync();
        }

        var fakeTime = new FakeTimeProvider(baseTime);
        var worker = new LifecycleTimerWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            fakeTime,
            NullLogger<LifecycleTimerWorker>.Instance);
        await worker.TickAsync(CancellationToken.None);

        await using var scope2 = factory.Services.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<PricingDbContext>();
        var c = await db2.Coupons.AsNoTracking().SingleAsync(c => c.Id == couponId);
        c.State.Should().Be(LifecycleState.Expired);
        c.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Tick_IsIdempotent_NoOpAfterFirstRun()
    {
        await factory.ResetDatabaseAsync();
        var baseTime = DateTimeOffset.UtcNow;
        var couponId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            db.Coupons.Add(new Coupon
            {
                Id = couponId,
                Code = "TIMER-IDEMP",
                Kind = "percent",
                Value = 5,
                CapMinor = 1_00_000,
                PerCustomerLimit = 1,
                OverallLimit = 100,
                ExcludesRestricted = false,
                StacksWithPromotions = true,
                MarketCodes = new[] { "sa" },
                ValidFrom = baseTime.AddSeconds(-30),
                ValidTo = baseTime.AddDays(7),
                DisplayInBanners = false,
                LabelAr = "نفس النتيجة",
                LabelEn = "Idempotency test",
                State = LifecycleState.Scheduled,
                StateChangedAtUtc = baseTime.AddDays(-1),
                StateChangedByActorId = CommercialPermissions.SystemActorId,
                AuthorActorId = CommercialPermissions.SystemActorId,
                IsActive = false,
                CreatedAt = baseTime.AddDays(-1),
                UpdatedAt = baseTime.AddDays(-1),
            });
            await db.SaveChangesAsync();
        }

        var fakeTime = new FakeTimeProvider(baseTime);
        var worker = new LifecycleTimerWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            fakeTime,
            NullLogger<LifecycleTimerWorker>.Instance);
        await worker.TickAsync(CancellationToken.None);
        await worker.TickAsync(CancellationToken.None); // second tick is no-op

        await using var scope2 = factory.Services.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<PricingDbContext>();
        var c = await db2.Coupons.AsNoTracking().SingleAsync(c => c.Id == couponId);
        c.State.Should().Be(LifecycleState.Active,
            "the row should be active after the first tick and remain active after the second");
        var auditCount = await db2.CommercialAuditEvents.AsNoTracking()
            .CountAsync(e => e.TargetEntityId == couponId &&
                              e.Kind == "coupon.lifecycle_transitioned");
        auditCount.Should().Be(1, "idempotent re-tick must NOT write a second transition audit row");
    }
}
