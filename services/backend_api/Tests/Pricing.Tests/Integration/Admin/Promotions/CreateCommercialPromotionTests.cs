using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.Promotions;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin.Promotions;

/// <summary>
/// Spec 007-b T069/T072 — Acceptance Scenarios 1, 2, 4 from spec.md User Story 2
/// (create + bilingual labels + BOGO/bundle target_sku validation + DELETE-forbidden).
/// Schedule + overlap + active-state lock have dedicated test files.
/// </summary>
[Collection("pricing-fixture")]
public sealed class CreateCommercialPromotionTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Create_HappyPath_PercentOff_PersistsDraftWithBilingualLabel()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var resp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/promotions", BuildValidPercentRequest());

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<CommercialPromotionResponse>();
        body!.State.Should().Be("draft");
        body.Kind.Should().Be("percent_off");
        body.PercentOff.Should().Be(15);
        body.Label.Ar.Should().Be("تخفيضات الربيع");
        body.Label.En.Should().Be("Spring Sale");
        body.RowVersion.Should().BeGreaterThan(0u);

        // Audit row written to pricing.commercial_audit_events (Principle 25).
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var auditRows = await db.CommercialAuditEvents
            .Where(e => e.TargetEntityId == body.Id)
            .ToListAsync();
        auditRows.Should().ContainSingle().Which.Kind.Should().Be("promotion.created");

        // ConfigJson is rebuilt so the engine still resolves the value.
        var promo = await db.Promotions.FirstAsync(p => p.Id == body.Id);
        promo.ConfigJson.Should().Contain("percentBps");
        promo.ConfigJson.Should().Contain("1500");  // 15% * 100 = 1500 bps
    }

    [Fact]
    public async Task Create_MissingArOrEnLabel_Returns400_With_BilingualReason()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var req = BuildValidPercentRequest() with { Label = new("", "Spring Sale") };

        var resp = await client.PostAsJsonAsync("/v1/admin/commercial/promotions", req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("promotion.label.required_bilingual");
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
        var req = BuildValidPercentRequest() with
        {
            ValidFrom = now.AddDays(10),
            ValidTo = now.AddDays(1),
        };

        var resp = await client.PostAsJsonAsync("/v1/admin/commercial/promotions", req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("promotion.schedule.invalid_window");
    }

    [Fact]
    public async Task Create_BogoWithoutRewardSku_Returns400_With_TargetSkuInvalidReason()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var req = BuildValidPercentRequest() with
        {
            Kind = "bogo",
            PercentOff = null,
            RewardSku = null,
        };

        var resp = await client.PostAsJsonAsync("/v1/admin/commercial/promotions", req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("promotion.target_sku_invalid");
    }

    [Fact]
    public async Task Create_BundleWithoutBundleSku_Returns400_With_TargetSkuInvalidReason()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var req = BuildValidPercentRequest() with
        {
            Kind = "bundle",
            PercentOff = null,
            BundleSku = null,
        };

        var resp = await client.PostAsJsonAsync("/v1/admin/commercial/promotions", req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("promotion.target_sku_invalid");
    }

    [Fact]
    public async Task Create_AppliesToTooMany_Returns400_With_TooManyReason()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var req = BuildValidPercentRequest() with
        {
            AppliesToProductIds = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray(),
        };

        var resp = await client.PostAsJsonAsync("/v1/admin/commercial/promotions", req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("promotion.applies_to.too_many");
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
            "/v1/admin/commercial/promotions", BuildValidPercentRequest());
        var body = await create.Content.ReadFromJsonAsync<CommercialPromotionResponse>();

        var resp = await client.DeleteAsync($"/v1/admin/commercial/promotions/{body!.Id:N}");
        resp.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("commercial.row.delete_forbidden");
    }

    private static CreateCommercialPromotionRequest BuildValidPercentRequest() => new(
        Kind: "percent_off",
        Markets: new[] { "ksa", "eg" },
        PercentOff: 15,
        AmountOffMinor: null,
        RewardSku: null,
        BundleSku: null,
        AppliesToProductIds: null,
        AppliesToCategoryIds: null,
        Priority: 100,
        ValidFrom: DateTimeOffset.UtcNow.AddDays(1),
        ValidTo: DateTimeOffset.UtcNow.AddDays(31),
        StacksWithCoupons: true,
        StacksWithOtherPromotions: true,
        BannerEligible: false,
        Label: new("تخفيضات الربيع", "Spring Sale"),
        Description: new("خصم 15% على جميع منتجات الربيع", "15% off all spring products"));
}
