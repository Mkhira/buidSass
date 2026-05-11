using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.Coupons;
using BackendApi.Modules.Pricing.Authorization;
using FluentAssertions;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin.Coupons;

/// <summary>
/// Spec 007-b T052 — covers Acceptance Scenario 3 from User Story 1
/// (future valid_from → row persists in scheduled state) and the high-impact
/// gate fork (FR-025) wired into the Schedule path. Timer-driven flip from
/// scheduled → active is exercised by the LifecycleTimerWorker tests landing
/// in the Polish phase.
/// </summary>
[Collection("pricing-fixture")]
public sealed class ScheduleCommercialCouponTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Schedule_FutureValidFrom_TransitionsToScheduled()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var create = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons",
            BuildSubthresholdRequest("SCHEDFUT", validFrom: DateTimeOffset.UtcNow.AddDays(2)));
        var draft = await create.Content.ReadFromJsonAsync<CommercialCouponResponse>();
        draft!.State.Should().Be("draft");

        var sched = await client.PostAsync(
            $"/v1/admin/commercial/coupons/{draft.Id:N}/schedule", null);
        sched.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await sched.Content.ReadFromJsonAsync<CommercialCouponResponse>();
        body!.State.Should().Be("scheduled");
    }

    [Fact]
    public async Task Schedule_PastValidFrom_TransitionsToActive()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var create = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons",
            BuildSubthresholdRequest("SCHEDPAST", validFrom: DateTimeOffset.UtcNow.AddMinutes(-1)));
        var draft = await create.Content.ReadFromJsonAsync<CommercialCouponResponse>();

        var sched = await client.PostAsync(
            $"/v1/admin/commercial/coupons/{draft!.Id:N}/schedule", null);
        sched.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await sched.Content.ReadFromJsonAsync<CommercialCouponResponse>();
        body!.State.Should().Be("active");
    }

    [Fact]
    public async Task Schedule_TripsHighImpactGate_Returns403_With_RequiresApproval()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        // Tripping criterion 4: schedule-duration-days >= threshold (default 14).
        // Setting both usage limits to null also trips criterion 3 — but we use
        // duration here so we can vary it from the sub-threshold case.
        var req = BuildSubthresholdRequest(
                "GATEHIGH",
                validFrom: DateTimeOffset.UtcNow.AddDays(1))
            with
            { ValidTo = DateTimeOffset.UtcNow.AddDays(20) };

        var create = await client.PostAsJsonAsync("/v1/admin/commercial/coupons", req);
        var draft = await create.Content.ReadFromJsonAsync<CommercialCouponResponse>();

        var sched = await client.PostAsync(
            $"/v1/admin/commercial/coupons/{draft!.Id:N}/schedule", null);
        sched.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sched.Content.ReadAsStringAsync()).Should().Contain("coupon.activation.requires_approval");
    }

    [Fact]
    public async Task Schedule_FromNonDraftState_Returns400_With_InvalidTransition()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var create = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons",
            BuildSubthresholdRequest("DOUBLE", validFrom: DateTimeOffset.UtcNow.AddMinutes(-1)));
        var draft = await create.Content.ReadFromJsonAsync<CommercialCouponResponse>();
        await client.PostAsync($"/v1/admin/commercial/coupons/{draft!.Id:N}/schedule", null);

        // Re-attempting schedule from an active row must fail with the canonical reason.
        var second = await client.PostAsync(
            $"/v1/admin/commercial/coupons/{draft.Id:N}/schedule", null);
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await second.Content.ReadAsStringAsync()).Should().Contain("commercial.row.invalid_transition");
    }

    /// <summary>
    /// Build a request that does NOT trip the high-impact gate so the schedule
    /// path can complete without an approval cosign:
    /// - percent_off=5 (below the 30 default threshold)
    /// - cap_minor below the SA 5 000 000 amount threshold (we set 1 000)
    /// - both per_customer_limit and overall_limit set (criterion 3 not tripped)
    /// - duration < 14 days (criterion 4 not tripped)
    /// </summary>
    private static CreateCommercialCouponRequest BuildSubthresholdRequest(
        string code, DateTimeOffset validFrom) => new(
        Code: code,
        Markets: new[] { "ksa" },
        Type: "percent_off",
        Value: 5,
        AmountOffMinor: null,
        CapMinor: 1_000,
        PerCustomerLimit: 1,
        OverallLimit: 100,
        ExcludesRestricted: false,
        ValidFrom: validFrom,
        ValidTo: validFrom.AddDays(7),
        StacksWithPromotions: true,
        DisplayInBanners: false,
        Label: new("خصم تجريبي", "Test discount"),
        Description: null);
}
