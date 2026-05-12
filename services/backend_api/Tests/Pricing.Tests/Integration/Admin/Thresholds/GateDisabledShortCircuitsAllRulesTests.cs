using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.Coupons;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin.Thresholds;

/// <summary>
/// Spec 007-b T108 — when <c>gate_enabled=false</c> for a market, no draft
/// (regardless of size) trips the high-impact gate. Operators may activate
/// directly. This is the global kill-switch surface from FR-025.
///
/// The test wraps the gate mutation in a try/finally so subsequent tests in the
/// shared <c>pricing-fixture</c> collection observe a re-enabled gate; the
/// thresholds table is NOT part of <see cref="PricingTestFactory.ResetDatabaseAsync"/>'s
/// truncate set (other tests rely on the seeded values).
/// </summary>
[Collection("pricing-fixture")]
public sealed class GateDisabledShortCircuitsAllRulesTests(PricingTestFactory factory)
{
    [Fact]
    public async Task GateDisabled_HighImpactDraft_SchedulesWithoutApproval()
    {
        await factory.ResetDatabaseAsync();
        await SetGateEnabledAsync("SA", enabled: false);
        try
        {
            var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
                factory, new[] { CommercialPermissions.Operator });
            var client = factory.CreateClient();
            PricingAdminAuthHelper.SetBearer(client, token);

            // Build a coupon that WOULD trip every criterion if the gate were on.
            var createResp = await client.PostAsJsonAsync(
                "/v1/admin/commercial/coupons",
                new CreateCommercialCouponRequest(
                    Code: "GATE-OFF",
                    Markets: new[] { "ksa" },
                    Type: "percent_off",
                    Value: 60,                              // > 30%
                    AmountOffMinor: null,
                    CapMinor: 10_000_000,                   // > 5M minor
                    PerCustomerLimit: 1,
                    OverallLimit: 1000,
                    ExcludesRestricted: true,
                    ValidFrom: DateTimeOffset.UtcNow.AddDays(-1),
                    ValidTo: DateTimeOffset.UtcNow.AddDays(30), // > 14d
                    StacksWithPromotions: true,
                    DisplayInBanners: false,
                    Label: new("اختبار مفتاح الإغلاق", "Kill-switch test"),
                    Description: null));
            createResp.StatusCode.Should().Be(HttpStatusCode.Created);
            var coupon = (await createResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;

            var schedResp = await client.PostAsJsonAsync(
                $"/v1/admin/commercial/coupons/{coupon.Id:D}/schedule", new { });

            schedResp.StatusCode.Should().Be(HttpStatusCode.OK,
                "with gate_enabled=false, no draft trips the gate — operator can self-activate");
            var scheduled = (await schedResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;
            scheduled.State.Should().Be("active");
        }
        finally
        {
            // Always re-enable the gate so other tests in the shared fixture
            // observe the seeded baseline.
            await SetGateEnabledAsync("SA", enabled: true);
        }
    }

    private async Task SetGateEnabledAsync(string marketCode, bool enabled)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var row = await db.CommercialThresholds
            .SingleAsync(t => t.MarketCode == marketCode);
        row.GateEnabled = enabled;
        await db.SaveChangesAsync();
    }
}
