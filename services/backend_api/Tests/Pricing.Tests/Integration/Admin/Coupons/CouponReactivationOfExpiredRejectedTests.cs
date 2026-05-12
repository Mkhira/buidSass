using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.Coupons;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin.Coupons;

/// <summary>
/// Spec 007-b T123 — reactivating a coupon that has already passed into the
/// terminal <c>expired</c> state is refused with reason
/// <c>commercial.reactivation.expired_terminal</c> (FR-016 / US7).
/// </summary>
[Collection("pricing-fixture")]
public sealed class CouponReactivationOfExpiredRejectedTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Reactivate_OfExpiredCoupon_Returns400_With_ExpiredTerminal()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var couponId = await SeedExpiredCouponAsync();

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{couponId:D}/reactivate",
            new { ReasonNote = "operator attempts to revive an expired coupon" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync())
            .Should().Contain(CommercialReasonCode.CommercialReactivationExpiredTerminal);
    }

    private async Task<Guid> SeedExpiredCouponAsync()
    {
        var id = Guid.NewGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        db.Coupons.Add(new Coupon
        {
            Id = id,
            Code = $"EXP-{Guid.NewGuid().ToString("N")[..8]}",
            Kind = "percent",
            Value = 5,
            CapMinor = 1_00_000,
            PerCustomerLimit = 1,
            OverallLimit = 1,
            ExcludesRestricted = false,
            StacksWithPromotions = true,
            MarketCodes = new[] { "sa" },
            // ValidTo strictly before ValidFrom-style "long past" — already expired.
            ValidFrom = DateTimeOffset.UtcNow.AddDays(-90),
            ValidTo = DateTimeOffset.UtcNow.AddDays(-30),
            DisplayInBanners = false,
            LabelAr = "كوبون منتهي",
            LabelEn = "Expired coupon",
            State = LifecycleState.Expired,
            StateChangedAtUtc = DateTimeOffset.UtcNow.AddDays(-30),
            StateChangedByActorId = CommercialPermissions.SystemActorId,
            AuthorActorId = CommercialPermissions.SystemActorId,
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-90),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        });
        await db.SaveChangesAsync();
        return id;
    }
}
