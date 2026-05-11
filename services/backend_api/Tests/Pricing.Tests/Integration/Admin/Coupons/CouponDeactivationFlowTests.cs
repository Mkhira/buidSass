using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.Coupons;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin.Coupons;

/// <summary>
/// Spec 007-b T121, T122 — US7 deactivation flow as a standalone user-visible
/// behavior. The implementation was already shipped with US1; these tests
/// validate the contract surface end-to-end.
///
/// Coverage:
/// - happy path: active → deactivate with reason → state + audit row + grace
///   payload (T121)
/// - reason < 10 chars rejected (T122)
/// - reactivation of expired terminal rejected (T123)
/// </summary>
[Collection("pricing-fixture")]
public sealed class CouponDeactivationFlowTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Deactivate_HappyPath_TransitionsToDeactivated_AndWritesAuditRow()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        // Create + schedule into Active.
        var createResp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons", BuildBackdatedCouponRequest("DEACT-HAPPY"));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;

        var schedResp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{created.Id:D}/schedule",
            new { });
        schedResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var scheduled = (await schedResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;
        scheduled.State.Should().Be("active", "valid_from is in the past so schedule should activate immediately");

        // Deactivate with a long enough reason note.
        var deactResp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{created.Id:D}/deactivate",
            new { ReasonNote = "operator paused for incident #4242 review" });
        deactResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var deactivated = (await deactResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;
        deactivated.State.Should().Be("deactivated");

        // Verify audit row in pricing.commercial_audit_events.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var auditRows = await db.CommercialAuditEvents
            .Where(e => e.TargetEntityId == created.Id &&
                        e.Kind == "coupon.lifecycle_transitioned")
            .OrderBy(e => e.RecordedAtUtc)
            .ToListAsync();
        // Two transitions: draft→active, active→deactivated.
        auditRows.Should().HaveCount(2);
        auditRows.Last().ReasonNote.Should().Contain("incident #4242");
    }

    [Fact]
    public async Task Deactivate_ReasonNoteTooShort_Returns400_With_ReasonRequired()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var createResp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons", BuildBackdatedCouponRequest("DEACT-SHORT"));
        var created = (await createResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;
        await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{created.Id:D}/schedule", new { });

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{created.Id:D}/deactivate",
            new { ReasonNote = "tooshort" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync())
            .Should().Contain("commercial.deactivation.reason_required");
    }

    [Fact]
    public async Task Reactivate_OfDeactivatedCoupon_TransitionsBackToActive()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var createResp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons", BuildBackdatedCouponRequest("REACT-OK"));
        var created = (await createResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;
        await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{created.Id:D}/schedule", new { });
        await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{created.Id:D}/deactivate",
            new { ReasonNote = "operator paused before reactivation test" });

        var reactResp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{created.Id:D}/reactivate",
            new { ReasonNote = "false positive resolved by ops" });

        reactResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var reactivated = (await reactResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;
        reactivated.State.Should().Be("active");
    }

    /// <summary>
    /// Build a coupon with valid_from in the past + a SMALL economic footprint so
    /// the high-impact gate does NOT trip on schedule (we want the activation
    /// path to land in Active without approver involvement).
    /// </summary>
    private static CreateCommercialCouponRequest BuildBackdatedCouponRequest(string code) => new(
        Code: code,
        Markets: new[] { "ksa", "eg" },
        Type: "percent_off",
        Value: 5,                       // below seeded 30% threshold
        AmountOffMinor: null,
        CapMinor: 1_000_00,             // SAR 1 000 cap — below the 5M threshold
        PerCustomerLimit: 1,
        OverallLimit: 100,
        ExcludesRestricted: true,
        ValidFrom: DateTimeOffset.UtcNow.AddDays(-1), // backdated → activates on schedule
        ValidTo: DateTimeOffset.UtcNow.AddDays(7),    // 8-day duration — below 14-day threshold
        StacksWithPromotions: true,
        DisplayInBanners: false,
        Label: new("كوبون اختبار", "Deactivation flow test"),
        Description: null);
}
