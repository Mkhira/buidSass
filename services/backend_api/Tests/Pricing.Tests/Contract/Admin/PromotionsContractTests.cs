using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Contract.Admin;

[Collection("pricing-fixture")]
public sealed class PromotionsContractTests(PricingTestFactory factory)
{
    [Fact]
    public async Task CreateAndListPromotion_WritesAudit()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { "pricing.promotion.read", "pricing.promotion.write" });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var create = await client.PostAsJsonAsync("/v1/admin/pricing/promotions", new
        {
            kind = "percent_off",
            name = "Winter Sale",
            configJson = """{"percentBps":1000}""",
            appliesToProductIds = (Guid[]?)null,
            appliesToCategoryIds = (Guid[]?)null,
            marketCodes = new[] { "ksa" },
            priority = 10,
            startsAt = (DateTimeOffset?)null,
            endsAt = (DateTimeOffset?)null,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await client.GetAsync("/v1/admin/pricing/promotions");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogEntries.CountAsync(a => a.Action == "pricing.promotion.created"))
            .Should().BeGreaterThanOrEqualTo(1);
    }
}
