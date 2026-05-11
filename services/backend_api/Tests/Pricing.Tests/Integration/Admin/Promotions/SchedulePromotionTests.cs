using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.Promotions;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin.Promotions;

/// <summary>
/// Spec 007-b T070 — schedule + SKU-overlap warning + acknowledgement loop
/// (Acceptance Scenarios 3+5 from US2).
/// </summary>
[Collection("pricing-fixture")]
public sealed class SchedulePromotionTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Schedule_FutureValidFrom_TransitionsToScheduled()
    {
        await factory.ResetDatabaseAsync();
        var client = await BuildOperatorClientAsync();

        var (id, _) = await CreatePromoAsync(client, validFromOffset: TimeSpan.FromDays(2));

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/promotions/{id:N}/schedule",
            new CommercialPromotionScheduleRequest(null));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<CommercialPromotionResponse>();
        body!.State.Should().Be("scheduled");
    }

    [Fact]
    public async Task Schedule_PastValidFrom_TransitionsToActive()
    {
        await factory.ResetDatabaseAsync();
        var client = await BuildOperatorClientAsync();

        var (id, _) = await CreatePromoAsync(
            client,
            validFromOffset: TimeSpan.FromMinutes(-5),
            validToOffset: TimeSpan.FromDays(7));  // < 14-day high-impact threshold

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/promotions/{id:N}/schedule",
            new CommercialPromotionScheduleRequest(null));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<CommercialPromotionResponse>();
        body!.State.Should().Be("active");
    }

    [Fact]
    public async Task Schedule_FromNonDraft_Rejected()
    {
        await factory.ResetDatabaseAsync();
        var client = await BuildOperatorClientAsync();

        var (id, _) = await CreatePromoAsync(client, validFromOffset: TimeSpan.FromDays(2));
        // First schedule succeeds → scheduled.
        await client.PostAsJsonAsync(
            $"/v1/admin/commercial/promotions/{id:N}/schedule",
            new CommercialPromotionScheduleRequest(null));

        // Second schedule is rejected as invalid transition.
        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/promotions/{id:N}/schedule",
            new CommercialPromotionScheduleRequest(null));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("commercial.row.invalid_transition");
    }

    [Fact]
    public async Task Schedule_NonStackingPromotion_OverlapWithoutAck_Returns400_WithOverlapIds()
    {
        await factory.ResetDatabaseAsync();
        var client = await BuildOperatorClientAsync();
        var sku = Guid.NewGuid();

        // Existing active promo on the same SKU.
        var (existingId, _) = await CreatePromoAsync(
            client,
            appliesToProductIds: new[] { sku },
            validFromOffset: TimeSpan.FromMinutes(-5));
        await client.PostAsJsonAsync(
            $"/v1/admin/commercial/promotions/{existingId:N}/schedule",
            new CommercialPromotionScheduleRequest(null));

        // New non-stacking promo with overlapping window on the same SKU.
        var (newId, _) = await CreatePromoAsync(
            client,
            appliesToProductIds: new[] { sku },
            validFromOffset: TimeSpan.FromHours(1),
            stacksWithOtherPromotions: false);

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/promotions/{newId:N}/schedule",
            new CommercialPromotionScheduleRequest(AcknowledgeOverlap: null));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("promotion.overlap.warning");
        body.Should().Contain(existingId.ToString());
    }

    [Fact]
    public async Task Schedule_NonStackingPromotion_OverlapWithAck_Succeeds()
    {
        await factory.ResetDatabaseAsync();
        var client = await BuildOperatorClientAsync();
        var sku = Guid.NewGuid();

        var (existingId, _) = await CreatePromoAsync(
            client,
            appliesToProductIds: new[] { sku },
            validFromOffset: TimeSpan.FromMinutes(-5));
        await client.PostAsJsonAsync(
            $"/v1/admin/commercial/promotions/{existingId:N}/schedule",
            new CommercialPromotionScheduleRequest(null));

        var (newId, _) = await CreatePromoAsync(
            client,
            appliesToProductIds: new[] { sku },
            validFromOffset: TimeSpan.FromHours(1),
            stacksWithOtherPromotions: false);

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/promotions/{newId:N}/schedule",
            new CommercialPromotionScheduleRequest(AcknowledgeOverlap: true));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_PricingFieldWhileActive_Returns400_With_LockedReason()
    {
        await factory.ResetDatabaseAsync();
        var client = await BuildOperatorClientAsync();

        var (id, _) = await CreatePromoAsync(
            client,
            validFromOffset: TimeSpan.FromMinutes(-5));
        await client.PostAsJsonAsync(
            $"/v1/admin/commercial/promotions/{id:N}/schedule",
            new CommercialPromotionScheduleRequest(null));

        // PATCH should reject percent_off changes while active.
        var patch = new UpdateCommercialPromotionRequest(
            Kind: null, PercentOff: 50, AmountOffMinor: null,
            RewardSku: null, BundleSku: null,
            AppliesToProductIds: null, AppliesToCategoryIds: null,
            Markets: null, Priority: null,
            ValidFrom: null, ValidTo: null,
            StacksWithCoupons: null, StacksWithOtherPromotions: null,
            BannerEligible: null, Label: null, Description: null);

        var resp = await client.PatchAsJsonAsync($"/v1/admin/commercial/promotions/{id:N}", patch);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("promotion.locked.active_pricing_field");
    }

    [Fact]
    public async Task Update_NonPricingFieldWhileActive_Succeeds()
    {
        await factory.ResetDatabaseAsync();
        var client = await BuildOperatorClientAsync();

        var (id, _) = await CreatePromoAsync(
            client,
            validFromOffset: TimeSpan.FromMinutes(-5));
        await client.PostAsJsonAsync(
            $"/v1/admin/commercial/promotions/{id:N}/schedule",
            new CommercialPromotionScheduleRequest(null));

        // Banner-eligible toggle is non-pricing and allowed while active.
        var patch = new UpdateCommercialPromotionRequest(
            Kind: null, PercentOff: null, AmountOffMinor: null,
            RewardSku: null, BundleSku: null,
            AppliesToProductIds: null, AppliesToCategoryIds: null,
            Markets: null, Priority: null,
            ValidFrom: null, ValidTo: null,
            StacksWithCoupons: null, StacksWithOtherPromotions: null,
            BannerEligible: true,
            Label: new CommercialPromotionBilingual("اسم جديد", "New name"),
            Description: null);

        var resp = await client.PatchAsJsonAsync($"/v1/admin/commercial/promotions/{id:N}", patch);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<CommercialPromotionResponse>();
        body!.BannerEligible.Should().BeTrue();
        body.Label.En.Should().Be("New name");
    }

    private async Task<HttpClient> BuildOperatorClientAsync()
    {
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);
        return client;
    }

    private async Task<(Guid Id, CommercialPromotionResponse Body)> CreatePromoAsync(
        HttpClient client,
        TimeSpan? validFromOffset = null,
        TimeSpan? validToOffset = null,
        Guid[]? appliesToProductIds = null,
        bool stacksWithOtherPromotions = true)
    {
        var now = DateTimeOffset.UtcNow;
        var validFrom = now.Add(validFromOffset ?? TimeSpan.FromDays(1));
        // Default window is 7 days from validFrom — well under the seeded
        // high-impact threshold of 14 days, so a 10% promo does not trip the
        // gate in test scenarios that do not specifically exercise the gate.
        var validTo = validToOffset is { } off ? now.Add(off) : validFrom.AddDays(7);
        var req = new CreateCommercialPromotionRequest(
            Kind: "percent_off",
            Markets: new[] { "ksa" },
            PercentOff: 10,
            AmountOffMinor: null,
            RewardSku: null,
            BundleSku: null,
            AppliesToProductIds: appliesToProductIds,
            AppliesToCategoryIds: null,
            Priority: 100,
            ValidFrom: validFrom,
            ValidTo: validTo,
            StacksWithCoupons: true,
            StacksWithOtherPromotions: stacksWithOtherPromotions,
            BannerEligible: false,
            Label: new("اسم العرض", "Promo name"),
            Description: null);
        var resp = await client.PostAsJsonAsync("/v1/admin/commercial/promotions", req);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<CommercialPromotionResponse>();
        return (body!.Id, body);
    }
}
