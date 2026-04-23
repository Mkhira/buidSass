using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Contract.Admin;

[Collection("pricing-fixture")]
public sealed class TaxRatesContractTests(PricingTestFactory factory)
{
    [Fact]
    public async Task CreateTaxRate_AuditRowWritten()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { "pricing.tax.read", "pricing.tax.write" });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var resp = await client.PostAsJsonAsync("/v1/admin/pricing/tax-rates", new
        {
            marketCode = "ksa",
            kind = "vat",
            rateBps = 1500,
            effectiveFrom = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            effectiveTo = (DateTimeOffset?)null,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditRows = await db.AuditLogEntries.CountAsync(a => a.Action == "pricing.tax_rate.created");
        auditRows.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task CreateTaxRate_WithoutPermission_Forbidden()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { "pricing.tax.read" });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var resp = await client.PostAsJsonAsync("/v1/admin/pricing/tax-rates", new
        {
            marketCode = "ksa", kind = "vat", rateBps = 1500, effectiveFrom = DateTimeOffset.UtcNow,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
