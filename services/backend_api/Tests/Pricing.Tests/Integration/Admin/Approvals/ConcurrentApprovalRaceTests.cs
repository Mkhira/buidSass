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
/// Spec 007-b T105 (R12 layer 2) — when two approvers race a co-sign on the
/// same draft, only ONE approval row persists and only ONE activation occurs.
/// The losing call returns 409 with
/// <c>commercial.approval.already_recorded</c>. The unique
/// <c>(target_entity_kind, target_entity_id)</c> index on
/// <c>pricing.commercial_approvals</c> is the DB layer enforcer; the handler
/// catches the unique-violation and maps it to the reason code.
/// </summary>
[Collection("pricing-fixture")]
public sealed class ConcurrentApprovalRaceTests(PricingTestFactory factory)
{
    [Fact]
    public async Task TwoApprovers_RaceOnSameDraft_OnlyOneApprovalPersists()
    {
        await factory.ResetDatabaseAsync();

        // 1) Operator authors a high-impact draft.
        var (operatorToken, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator }, roleCode: "race.operator");
        var opClient = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(opClient, operatorToken);

        var couponResp = await opClient.PostAsJsonAsync(
            "/v1/admin/commercial/coupons",
            new CreateCommercialCouponRequest(
                Code: "RACE_HI",
                Markets: new[] { "ksa" },
                Type: "percent_off",
                Value: 50,                           // > 30%
                AmountOffMinor: null,
                CapMinor: 1_00_000,
                PerCustomerLimit: 1,
                OverallLimit: 100,
                ExcludesRestricted: true,
                ValidFrom: DateTimeOffset.UtcNow.AddDays(-1),
                ValidTo: DateTimeOffset.UtcNow.AddDays(5),
                StacksWithPromotions: true,
                DisplayInBanners: false,
                Label: new("سباق الموافقات", "Approval race"),
                Description: null));
        couponResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var coupon = (await couponResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;

        // 2) Two separate approvers.
        var (a1Token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Approver }, roleCode: "race.approverA");
        var (a2Token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Approver }, roleCode: "race.approverB");

        var a1Client = factory.CreateClient();
        var a2Client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(a1Client, a1Token);
        PricingAdminAuthHelper.SetBearer(a2Client, a2Token);

        var body = new RecordApprovalRequest(
            "coupon", coupon.Id, "concurrent approval race scenario");

        // 3) Fire both requests in parallel. Whichever lands second must lose.
        var taskA = a1Client.PostAsJsonAsync("/v1/admin/commercial/approvals", body);
        var taskB = a2Client.PostAsJsonAsync("/v1/admin/commercial/approvals", body);
        var responses = await Task.WhenAll(taskA, taskB);
        var statuses = new[] { responses[0].StatusCode, responses[1].StatusCode };
        statuses.Should().Contain(HttpStatusCode.OK, "one approval must succeed");
        statuses.Should().Contain(c => c == HttpStatusCode.Conflict || c == HttpStatusCode.BadRequest,
            "the loser must be refused with 409 (unique-violation) or 400 (no-longer-pending)");

        // Assert at most one approval row exists for the coupon.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var approvals = await db.CommercialApprovals.AsNoTracking()
            .Where(a => a.TargetEntityId == coupon.Id)
            .ToListAsync();
        approvals.Should().HaveCount(1,
            "the unique (target_entity_kind, target_entity_id) index must prevent duplicates");
    }
}
