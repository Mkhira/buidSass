using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.BusinessPricing;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin.BusinessPricing;

/// <summary>
/// Spec 007-b T081 / T082 / T083 — tier-row + company-override authoring
/// integration tests for User Story 3. Validates contract §4.1 / §4.2 plus
/// the engine-resolution invariant from spec.md acceptance Scenario.
/// </summary>
[Collection("pricing-fixture")]
public sealed class UpsertTierRowTests(PricingTestFactory factory)
{
    [Fact]
    public async Task UpsertTierRow_HappyPath_PersistsActiveRow()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var tierId = await PricingTestSeedHelper.CreateTierAsync(factory.Services, "tier-2");
        var productId = Guid.NewGuid();

        var resp = await client.PutAsJsonAsync(
            "/v1/admin/commercial/business-pricing/tier-rows",
            new UpsertTierRowRequest(tierId, productId, "sa", 8800));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<BusinessPricingRowResponse>();
        body!.State.Should().Be("active");
        body.TierId.Should().Be(tierId);
        body.CompanyId.Should().BeNull();
        body.NetMinor.Should().Be(8800);
        body.MarketCode.Should().Be("sa");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var audit = await db.CommercialAuditEvents
            .Where(e => e.TargetEntityId == body.Id)
            .ToListAsync();
        audit.Should().ContainSingle().Which.Kind.Should().Be("business_pricing.row_changed");
    }

    [Fact]
    public async Task UpsertTierRow_SameKey_UpdatesNet_NotInserts()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var tierId = await PricingTestSeedHelper.CreateTierAsync(factory.Services, "tier-2");
        var productId = Guid.NewGuid();

        var first = await client.PutAsJsonAsync(
            "/v1/admin/commercial/business-pricing/tier-rows",
            new UpsertTierRowRequest(tierId, productId, "sa", 8800));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PutAsJsonAsync(
            "/v1/admin/commercial/business-pricing/tier-rows",
            new UpsertTierRowRequest(tierId, productId, "sa", 9000));
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var rows = await db.ProductTierPrices
            .Where(p => p.TierId == tierId && p.ProductId == productId && p.MarketCode == "sa")
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].NetMinor.Should().Be(9000);
    }

    [Fact]
    public async Task UpsertTierRow_OperatorWithoutB2BAuthoring_Returns403()
    {
        await factory.ResetDatabaseAsync();
        // Operator role only — no commercial.b2b_authoring permission.
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var tierId = await PricingTestSeedHelper.CreateTierAsync(factory.Services, "tier-2");
        var resp = await client.PutAsJsonAsync(
            "/v1/admin/commercial/business-pricing/tier-rows",
            new UpsertTierRowRequest(tierId, Guid.NewGuid(), "sa", 5000));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpsertTierRow_UnknownTier_Returns404()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var resp = await client.PutAsJsonAsync(
            "/v1/admin/commercial/business-pricing/tier-rows",
            new UpsertTierRowRequest(Guid.NewGuid(), Guid.NewGuid(), "sa", 5000));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("business_pricing.tier.not_found");
    }

    [Fact]
    public async Task UpsertTierRow_NegativeNet_Returns400()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var tierId = await PricingTestSeedHelper.CreateTierAsync(factory.Services, "tier-2");
        var resp = await client.PutAsJsonAsync(
            "/v1/admin/commercial/business-pricing/tier-rows",
            new UpsertTierRowRequest(tierId, Guid.NewGuid(), "sa", -1));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpsertCompanyOverride_HappyPath_PersistsRowWithCompanyIdAndNullTier()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var companyId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var resp = await client.PutAsJsonAsync(
            "/v1/admin/commercial/business-pricing/company-overrides",
            new UpsertCompanyOverrideRequest(companyId, productId, "sa", 6500, CopiedFromTierId: null));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<BusinessPricingRowResponse>();
        body!.CompanyId.Should().Be(companyId);
        body.TierId.Should().BeNull();
        body.CopiedFromTierId.Should().BeNull();
        body.NetMinor.Should().Be(6500);
    }

    [Fact]
    public async Task UpsertCompanyOverride_CopiedFromTier_RecordsAuditPointer()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var tierId = await PricingTestSeedHelper.CreateTierAsync(factory.Services, "tier-1");
        var companyId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var resp = await client.PutAsJsonAsync(
            "/v1/admin/commercial/business-pricing/company-overrides",
            new UpsertCompanyOverrideRequest(companyId, productId, "sa", 7200, CopiedFromTierId: tierId));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<BusinessPricingRowResponse>();
        body!.CompanyId.Should().Be(companyId);
        // copied_from_tier_id is set; the XOR check allows tier_id to be set too in this shape.
        body.CopiedFromTierId.Should().Be(tierId);
        body.TierId.Should().Be(tierId);
    }

    [Fact]
    public async Task CompanyOverride_AndTierRow_CanCoexist_ForSameProductAndMarket()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var tierId = await PricingTestSeedHelper.CreateTierAsync(factory.Services, "tier-2");
        var productId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        var tierResp = await client.PutAsJsonAsync(
            "/v1/admin/commercial/business-pricing/tier-rows",
            new UpsertTierRowRequest(tierId, productId, "sa", 8800));
        tierResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var overrideResp = await client.PutAsJsonAsync(
            "/v1/admin/commercial/business-pricing/company-overrides",
            new UpsertCompanyOverrideRequest(companyId, productId, "sa", 7500, CopiedFromTierId: null));
        overrideResp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var rows = await db.ProductTierPrices
            .Where(p => p.ProductId == productId && p.MarketCode == "sa")
            .ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().Contain(r => r.TierId == tierId && r.CompanyId == null && r.NetMinor == 8800);
        rows.Should().Contain(r => r.TierId == null && r.CompanyId == companyId && r.NetMinor == 7500);
    }

    [Fact]
    public async Task BusinessPricing_DeactivateThenReactivate_RoundTrips()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var tierId = await PricingTestSeedHelper.CreateTierAsync(factory.Services, "tier-2");
        var productId = Guid.NewGuid();

        var created = await client.PutAsJsonAsync(
            "/v1/admin/commercial/business-pricing/tier-rows",
            new UpsertTierRowRequest(tierId, productId, "sa", 8800));
        var body = await created.Content.ReadFromJsonAsync<BusinessPricingRowResponse>();

        var deactivate = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/business-pricing/{body!.Id}/deactivate",
            new DeactivateOrReactivateBusinessPricingRequest("phased pricing rollout sunset"));
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        var deactivated = await deactivate.Content.ReadFromJsonAsync<BusinessPricingRowResponse>();
        deactivated!.State.Should().Be("deactivated");

        var reactivate = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/business-pricing/{body.Id}/reactivate",
            new DeactivateOrReactivateBusinessPricingRequest("rollout resumed Q3"));
        reactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        var reactivated = await reactivate.Content.ReadFromJsonAsync<BusinessPricingRowResponse>();
        reactivated!.State.Should().Be("active");
    }

    [Fact]
    public async Task BusinessPricing_DeactivateWithShortNote_Returns400()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var tierId = await PricingTestSeedHelper.CreateTierAsync(factory.Services, "tier-2");
        var productId = Guid.NewGuid();

        var created = await client.PutAsJsonAsync(
            "/v1/admin/commercial/business-pricing/tier-rows",
            new UpsertTierRowRequest(tierId, productId, "sa", 8800));
        var body = await created.Content.ReadFromJsonAsync<BusinessPricingRowResponse>();

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/business-pricing/{body!.Id}/deactivate",
            new DeactivateOrReactivateBusinessPricingRequest("short"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("commercial.deactivation.reason_required");
    }

    [Fact]
    public async Task BusinessPricing_Delete_OnActiveRow_Returns405()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var tierId = await PricingTestSeedHelper.CreateTierAsync(factory.Services, "tier-2");
        var productId = Guid.NewGuid();

        var created = await client.PutAsJsonAsync(
            "/v1/admin/commercial/business-pricing/tier-rows",
            new UpsertTierRowRequest(tierId, productId, "sa", 8800));
        var body = await created.Content.ReadFromJsonAsync<BusinessPricingRowResponse>();

        var resp = await client.DeleteAsync(
            $"/v1/admin/commercial/business-pricing/{body!.Id}");

        resp.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("commercial.row.delete_forbidden");
    }
}
