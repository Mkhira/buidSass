using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.Approvals;
using BackendApi.Modules.Pricing.Admin.Coupons;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin.Approvals;

/// <summary>
/// Spec 007-b T104, T106 — approval queue end-to-end. Confirms:
/// - a draft tripping the gate cannot be self-scheduled (403)
/// - an approver records an approval, activating the draft
/// - both author + approver actor ids appear in the audit trail (T106)
/// - self-approval by the author is refused (T104 / R12 layer 1)
/// </summary>
[Collection("pricing-fixture")]
public sealed class RecordApprovalTests(PricingTestFactory factory)
{
    [Fact]
    public async Task HighImpactDraft_OperatorActivation_Returns403_RequiresApproval()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var createResp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons", BuildHighImpactCouponRequest("HI-IMPACT"));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;

        var schedResp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{created.Id:D}/schedule",
            new { });

        schedResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await schedResp.Content.ReadAsStringAsync())
            .Should().Contain("coupon.activation.requires_approval");
    }

    [Fact]
    public async Task Approver_RecordApproval_ActivatesDraft_AndAuditCarriesBothActors()
    {
        await factory.ResetDatabaseAsync();

        // Author = operator-only role.
        var (operatorToken, operatorId) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator }, roleCode: "platform.operator");
        var operatorClient = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(operatorClient, operatorToken);

        var createResp = await operatorClient.PostAsJsonAsync(
            "/v1/admin/commercial/coupons", BuildHighImpactCouponRequest("HI-OK"));
        var created = (await createResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;

        // Approver = different account, holds commercial.approver.
        var (approverToken, approverId) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Approver }, roleCode: "platform.approver");
        var approverClient = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(approverClient, approverToken);

        var recordResp = await approverClient.PostAsJsonAsync(
            "/v1/admin/commercial/approvals",
            new
            {
                target_entity_kind = "coupon",
                target_entity_id = created.Id,
                cosign_note = "Q2 plan approved; high-impact volume aligned",
            });

        recordResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var recorded = (await recordResp.Content.ReadFromJsonAsync<RecordApprovalResponse>())!;
        recorded.Activated.Should().BeTrue();
        recorded.NewState.Should().Be("active");

        // Confirm both actor ids appear in audit_log for this coupon.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var actors = await db.CommercialAuditEvents
            .Where(e => e.TargetEntityId == created.Id)
            .Select(e => e.ActorId)
            .Distinct()
            .ToListAsync();
        actors.Should().Contain(operatorId, "author actor appears in coupon.created audit row");
        actors.Should().Contain(approverId, "approver actor appears in coupon.lifecycle_transitioned audit row");

        // Plus the approval row itself.
        var approvalRows = await db.CommercialApprovals
            .Where(a => a.TargetEntityId == created.Id)
            .ToListAsync();
        approvalRows.Should().ContainSingle();
        approvalRows[0].AuthorActorId.Should().Be(operatorId);
        approvalRows[0].ApproverActorId.Should().Be(approverId);
    }

    [Fact]
    public async Task SelfApproval_ByAuthor_Returns403_With_SelfApprovalForbidden()
    {
        await factory.ResetDatabaseAsync();

        // Same caller holds both operator + approver.
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory,
            new[] { CommercialPermissions.Operator, CommercialPermissions.Approver });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var createResp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons", BuildHighImpactCouponRequest("SELF-SIGN"));
        var created = (await createResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;

        var recordResp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/approvals",
            new
            {
                target_entity_kind = "coupon",
                target_entity_id = created.Id,
                cosign_note = "I cosign my own draft — should be refused",
            });

        recordResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await recordResp.Content.ReadAsStringAsync())
            .Should().Contain("commercial.self_approval.forbidden");
    }

    /// <summary>
    /// Build a coupon that trips the seeded SA threshold (percent ≥ 30%
    /// OR cap_minor ≥ 5_000_000 OR duration ≥ 14 days). Here we use a
    /// 40% percent_off — guaranteed to trip Criterion 1.
    /// </summary>
    private static CreateCommercialCouponRequest BuildHighImpactCouponRequest(string code) => new(
        Code: code,
        Markets: new[] { "ksa" },
        Type: "percent_off",
        Value: 40,                          // > 30% threshold → gate trips
        AmountOffMinor: null,
        CapMinor: 1_000_00,
        PerCustomerLimit: 1,
        OverallLimit: 100,
        ExcludesRestricted: true,
        ValidFrom: DateTimeOffset.UtcNow.AddDays(-1), // backdated so activation lands in Active
        ValidTo: DateTimeOffset.UtcNow.AddDays(5),    // 6-day duration — below 14-day criterion
        StacksWithPromotions: true,
        DisplayInBanners: false,
        Label: new("اختبار العتبة", "Threshold test coupon"),
        Description: null);
}
