using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.Coupons;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin;

/// <summary>
/// Spec 007-b T124 — every <c>CouponDeactivated</c> notification published
/// from the deactivation handler carries an <c>in_flight_grace_seconds</c>
/// value sourced from the per-market <c>pricing.commercial_thresholds</c> row
/// (research §R4 / FR-003a). Spec 010 checkout consumes this payload to
/// extend the in-flight cart's grace window.
/// </summary>
[Collection("pricing-fixture")]
public sealed class InFlightGracePayloadTests(PricingTestFactory factory)
{
    [Fact]
    public async Task CouponDeactivation_PublishesEvent_WithMatchingGraceSeconds()
    {
        await factory.ResetDatabaseAsync();
        CapturingHandler.Reset();

        // Resolve the configured grace for the SA market.
        int expectedGrace;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            expectedGrace = (await db.CommercialThresholds.AsNoTracking()
                .SingleAsync(t => t.MarketCode == "SA"))
                .CouponInFlightGraceSeconds;
        }

        // Register the capturing handler on a per-test branch of the factory's
        // service provider. The factory's main MediatR registration only sweeps
        // the BackendApi assembly; the capturing handler lives in the test
        // assembly so we wire it explicitly here.
        var withCapture = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddTransient<INotificationHandler<CouponDeactivated>, CapturingHandler>();
            });
        });

        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = withCapture.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        // Create + schedule + deactivate a small SA coupon (gate stays off).
        var createResp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons",
            new CreateCommercialCouponRequest(
                Code: $"GRACE-{Guid.NewGuid().ToString("N")[..6]}",
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
                Label: new("اختبار النعمة", "Grace test"),
                Description: null));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var coupon = (await createResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;

        var scheduleResp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{coupon.Id:D}/schedule", new { });
        scheduleResp.IsSuccessStatusCode.Should().BeTrue(
            "the schedule step is the precondition for the deactivation that publishes the event");

        var deactResp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{coupon.Id:D}/deactivate",
            new { ReasonNote = "verify in-flight grace payload" });
        deactResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var ev = CapturingHandler.LastEvent;
        ev.Should().NotBeNull("CouponDeactivated must publish via IPublisher on the in-process bus");
        ev!.InFlightGraceSeconds.Should().Be(expectedGrace,
            "deactivation event payload must reflect the per-market threshold row (research §R4)");
        ev.CouponId.Should().Be(coupon.Id);
        ev.ReasonNote.Should().Contain("in-flight grace payload");
    }

    /// <summary>
    /// Static-state capturing handler that records the last
    /// <see cref="CouponDeactivated"/> notification. Registered via
    /// <see cref="MediatR"/> in <c>backend_api</c>'s assembly scan only if the
    /// assembly is in the AddMediatR sweep — we rely on the test
    /// <c>PricingTestFactory</c>'s <c>ConfigureWebHost</c> to surface this
    /// handler. Reset between tests via <see cref="Reset"/>.
    /// </summary>
    public sealed class CapturingHandler : INotificationHandler<CouponDeactivated>
    {
        public static CouponDeactivated? LastEvent { get; private set; }
        public static void Reset() => LastEvent = null;

        public Task Handle(CouponDeactivated notification, CancellationToken cancellationToken)
        {
            LastEvent = notification;
            return Task.CompletedTask;
        }
    }
}
