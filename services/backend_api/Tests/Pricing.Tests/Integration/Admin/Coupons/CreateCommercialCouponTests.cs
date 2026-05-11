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
/// Spec 007-b T051 — Acceptance Scenarios 1, 4, 5 from spec.md User Story 1
/// (duplicate code, bilingual labels, valid_to ≤ valid_from). Acceptance
/// Scenario 3 (future valid_from → scheduled state) is asserted in
/// <see cref="ScheduleCommercialCouponTests"/>; Scenario 2 (preview matches
/// runtime) is asserted in <see cref="PreviewMatchesRuntimeTests"/>.
/// </summary>
[Collection("pricing-fixture")]
public sealed class CreateCommercialCouponTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Create_HappyPath_PersistsDraftWithBilingualLabel()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var resp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons", BuildValidPercentRequest("RAMADAN10"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<CommercialCouponResponse>();
        body!.State.Should().Be("draft");
        body.Code.Should().Be("RAMADAN10");
        body.Label.Ar.Should().Be("خصم رمضان");
        body.Label.En.Should().Be("Ramadan discount");
        body.RowVersion.Should().BeGreaterThan(0u);

        // Audit row written to pricing.commercial_audit_events (Principle 25)
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var auditRows = await db.CommercialAuditEvents
            .Where(e => e.TargetEntityId == body.Id)
            .ToListAsync();
        auditRows.Should().ContainSingle().Which.Kind.Should().Be("coupon.created");
    }

    [Fact]
    public async Task Create_DuplicateCode_CaseInsensitive_Returns400_With_DuplicateReason()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var first = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons", BuildValidPercentRequest("WELCOME10"));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Lowercase variant must collide with the upper-cased canonical code (R6).
        var second = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons", BuildValidPercentRequest("welcome10"));

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await second.Content.ReadAsStringAsync()).Should().Contain("coupon.code.duplicate");
    }

    [Fact]
    public async Task Create_MissingArOrEnLabel_Returns400_With_BilingualReason()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var req = BuildValidPercentRequest("EIDONLYEN") with { Label = new("", "Eid only EN") };

        var resp = await client.PostAsJsonAsync("/v1/admin/commercial/coupons", req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("coupon.label.required_bilingual");
    }

    [Fact]
    public async Task Create_ValidToBeforeValidFrom_Returns400_With_InvalidWindowReason()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var now = DateTimeOffset.UtcNow;
        var req = BuildValidPercentRequest("BADWIN10") with
        {
            ValidFrom = now.AddDays(10),
            ValidTo = now.AddDays(1),  // before valid_from
        };

        var resp = await client.PostAsJsonAsync("/v1/admin/commercial/coupons", req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("coupon.schedule.invalid_window");
    }

    [Fact]
    public async Task Create_ValueOutOfRange_Returns400_With_ValueReason()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var req = BuildValidPercentRequest("OOR101") with { Value = 101 };  // > 100 percent

        var resp = await client.PostAsJsonAsync("/v1/admin/commercial/coupons", req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("coupon.value");
    }

    [Fact]
    public async Task Create_UsageLimitZero_Returns400_With_LimitReason()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var req = BuildValidPercentRequest("ZEROLIM") with { OverallLimit = 0 };

        var resp = await client.PostAsJsonAsync("/v1/admin/commercial/coupons", req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("coupon.limit.not_zero");
    }

    [Fact]
    public async Task Delete_AlwaysReturns405_With_DeleteForbiddenReason()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var create = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons", BuildValidPercentRequest("DELME10"));
        var body = await create.Content.ReadFromJsonAsync<CommercialCouponResponse>();

        var resp = await client.DeleteAsync($"/v1/admin/commercial/coupons/{body!.Id:N}");
        resp.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("commercial.row.delete_forbidden");
    }

    private static CreateCommercialCouponRequest BuildValidPercentRequest(string code) => new(
        Code: code,
        Markets: new[] { "ksa", "eg" },
        Type: "percent_off",
        Value: 10,
        AmountOffMinor: null,
        CapMinor: 5_000_00, // SAR 5 000 cap
        PerCustomerLimit: 1,
        OverallLimit: 10_000,
        ExcludesRestricted: true,
        ValidFrom: DateTimeOffset.UtcNow.AddDays(1),
        ValidTo: DateTimeOffset.UtcNow.AddDays(31),
        StacksWithPromotions: true,
        DisplayInBanners: false,
        Label: new("خصم رمضان", "Ramadan discount"),
        Description: new("ر.س 10 خصم على جميع المنتجات", "10% off all products"));
}
