using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Contract.Admin;

[Collection("pricing-fixture")]
public sealed class CouponsContractTests(PricingTestFactory factory)
{
    [Fact]
    public async Task CreateCoupon_DuplicateCode_Conflict()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { "pricing.coupon.read", "pricing.coupon.write" });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var payload = new
        {
            code = "DUP",
            kind = "percent",
            value = 1_000,
            capMinor = (long?)null,
            perCustomerLimit = (int?)null,
            overallLimit = (int?)null,
            excludesRestricted = false,
            marketCodes = new[] { "ksa" },
            validFrom = (DateTimeOffset?)null,
            validTo = (DateTimeOffset?)null,
        };

        var first = await client.PostAsJsonAsync("/v1/admin/pricing/coupons", payload);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync("/v1/admin/pricing/coupons", payload);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogEntries.CountAsync(a => a.Action == "pricing.coupon.created"))
            .Should().Be(1);
    }
}
