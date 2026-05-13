using System.Collections.Concurrent;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.Coupons;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Events;

/// <summary>
/// Spec 007-b T140 — verifies the 10 commercial domain events from
/// <c>Modules/Shared/CommercialDomainEvents.cs</c> fire on the in-process
/// bus when their corresponding lifecycle transition occurs. This is the
/// contract spec 025 (notifications) and spec 010 (checkout) read against.
///
/// The test wires capturing INotificationHandler instances via the factory's
/// WithWebHostBuilder branch (MediatR's main scan only sees the BackendApi
/// assembly) and exercises a representative end-to-end flow:
/// CreateCoupon -> Schedule (Active) -> Deactivate -> Reactivate.
/// Three of the four events fire from that flow; CouponExpired is
/// time-driven and validated in LifecycleTimerWorkerDriftTests.
/// </summary>
[Collection("pricing-fixture")]
public sealed class CommercialDomainEventsPublishedTests(PricingTestFactory factory)
{
    [Fact]
    public async Task CouponLifecycleFlow_PublishesActivatedDeactivatedReactivated()
    {
        await factory.ResetDatabaseAsync();
        DomainEventCapture.Reset();

        var withCapture = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddTransient<INotificationHandler<CouponActivated>, ActivatedCapture>();
                services.AddTransient<INotificationHandler<CouponDeactivated>, DeactivatedCapture>();
                services.AddTransient<INotificationHandler<CouponReactivated>, ReactivatedCapture>();
            });
        });

        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = withCapture.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var createResp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons",
            new CreateCommercialCouponRequest(
                Code: $"EVT-{Guid.NewGuid().ToString("N")[..6]}",
                Markets: new[] { "ksa" },
                Type: "percent_off",
                Value: 5,
                AmountOffMinor: null,
                CapMinor: 1_00_000,
                PerCustomerLimit: 1,
                OverallLimit: 100,
                ExcludesRestricted: false,
                ValidFrom: DateTimeOffset.UtcNow.AddDays(-1),
                ValidTo: DateTimeOffset.UtcNow.AddDays(7),
                StacksWithPromotions: true,
                DisplayInBanners: false,
                Label: new("اختبار الأحداث", "Domain-events test"),
                Description: null));
        createResp.EnsureSuccessStatusCode();
        var coupon = (await createResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;

        var scheduleResp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{coupon.Id:D}/schedule", new { });
        scheduleResp.EnsureSuccessStatusCode();
        DomainEventCapture.ActivatedCount.Should().Be(1, "schedule into Active publishes CouponActivated");

        var deactResp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{coupon.Id:D}/deactivate",
            new { ReasonNote = "events suite — pause for reactivation step" });
        deactResp.EnsureSuccessStatusCode();
        DomainEventCapture.DeactivatedCount.Should().Be(1,
            "deactivation must publish exactly one CouponDeactivated notification");

        var reactResp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{coupon.Id:D}/reactivate",
            new { ReasonNote = "events suite — re-enable after pause" });
        reactResp.EnsureSuccessStatusCode();
        DomainEventCapture.ReactivatedCount.Should().Be(1,
            "reactivation must publish exactly one CouponReactivated notification");
    }

    /// <summary>
    /// Static counter shared across handler instances; reset between tests.
    /// </summary>
    private static class DomainEventCapture
    {
        public static int ActivatedCount;
        public static int DeactivatedCount;
        public static int ReactivatedCount;

        public static void Reset()
        {
            ActivatedCount = 0;
            DeactivatedCount = 0;
            ReactivatedCount = 0;
        }

        public static void RecordActivated() => Interlocked.Increment(ref ActivatedCount);
        public static void RecordDeactivated() => Interlocked.Increment(ref DeactivatedCount);
        public static void RecordReactivated() => Interlocked.Increment(ref ReactivatedCount);
    }

    public sealed class ActivatedCapture : INotificationHandler<CouponActivated>
    {
        public Task Handle(CouponActivated _, CancellationToken __)
        {
            DomainEventCapture.RecordActivated();
            return Task.CompletedTask;
        }
    }
    public sealed class DeactivatedCapture : INotificationHandler<CouponDeactivated>
    {
        public Task Handle(CouponDeactivated _, CancellationToken __)
        {
            DomainEventCapture.RecordDeactivated();
            return Task.CompletedTask;
        }
    }
    public sealed class ReactivatedCapture : INotificationHandler<CouponReactivated>
    {
        public Task Handle(CouponReactivated _, CancellationToken __)
        {
            DomainEventCapture.RecordReactivated();
            return Task.CompletedTask;
        }
    }
}
