using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Pricing.Admin.Approvals;
using BackendApi.Modules.Pricing.Admin.Coupons;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using FluentAssertions;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Contract.Admin.Approvals;

/// <summary>
/// Spec 007-b T103 — contract-shape conformance for
/// <c>POST /v1/admin/commercial/approvals</c>. Pins the problem+json envelope
/// for the 5 owned reason codes from contract §8 — used by the admin UI to
/// localise messages and gate retries.
/// </summary>
[Collection("pricing-fixture")]
public sealed class RecordApprovalContractTests(PricingTestFactory factory)
{
    [Fact]
    public async Task NotPending_Returns400_WithReasonCode()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Approver });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        // Posting against a non-existent draft is the cleanest negative case.
        var resp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/approvals",
            new
            {
                target_entity_kind = "coupon",
                target_entity_id = Guid.NewGuid(),
                cosign_note = "Approver path: no draft exists at this id",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(400);
        root.GetProperty("reasonCode").GetString().Should().Be(
            CommercialReasonCode.CommercialApprovalNotPending);
    }

    [Fact]
    public async Task UnknownKind_Returns400_WithReasonCode()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Approver });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var resp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/approvals",
            new
            {
                target_entity_kind = "campaign",
                target_entity_id = Guid.NewGuid(),
                cosign_note = "campaigns are not gated by approvals",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("reasonCode").GetString().Should().Be(
            CommercialReasonCode.CommercialApprovalNotPending);
    }

    [Fact]
    public async Task CosignNoteTooShort_Returns400_WithCosignNoteRequired()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Approver });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var resp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/approvals",
            new
            {
                target_entity_kind = "coupon",
                target_entity_id = Guid.NewGuid(),
                cosign_note = "tooshort",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("cosign_note", "the validation error must name the offending field");
    }

    [Fact]
    public async Task Approval_HappyPath_Carries_NewState_And_Activated()
    {
        await factory.ResetDatabaseAsync();
        // Operator authors a high-impact draft.
        var (operatorToken, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator }, roleCode: "contract.operator");
        var opClient = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(opClient, operatorToken);

        var couponResp = await opClient.PostAsJsonAsync(
            "/v1/admin/commercial/coupons",
            new CreateCommercialCouponRequest(
                Code: "CONTRACT_HI",
                Markets: new[] { "ksa" },
                Type: "percent_off",
                Value: 40,                           // > 30% → high-impact
                AmountOffMinor: null,
                CapMinor: 1_00_000,
                PerCustomerLimit: 1,
                OverallLimit: 100,
                ExcludesRestricted: true,
                ValidFrom: DateTimeOffset.UtcNow.AddDays(-1),
                ValidTo: DateTimeOffset.UtcNow.AddDays(5),
                StacksWithPromotions: true,
                DisplayInBanners: false,
                Label: new("اختبار العقد", "Contract test"),
                Description: null));
        couponResp.StatusCode.Should().Be(HttpStatusCode.Created,
            "the coupon must be created before exercising the approval flow");
        var coupon = (await couponResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;
        coupon.Should().NotBeNull();

        // Different account holds the approver permission.
        var (approverToken, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Approver }, roleCode: "contract.approver");
        var apClient = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(apClient, approverToken);

        var resp = await apClient.PostAsJsonAsync(
            "/v1/admin/commercial/approvals",
            new RecordApprovalRequest("coupon", coupon.Id, "approver signs off Q2 plan"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.TryGetProperty("approval_id", out _).Should().BeTrue();
        root.GetProperty("activated").GetBoolean().Should().BeTrue();
        root.GetProperty("new_state").GetString().Should().Be("active");
    }
}
