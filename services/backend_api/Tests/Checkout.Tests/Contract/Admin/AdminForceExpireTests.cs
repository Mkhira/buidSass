using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using BackendApi.Modules.Shared;
using Checkout.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Tests.Contract.Admin;

/// <summary>US8 / SC-009 — admin force-expire writes an audit row.</summary>
[Collection("checkout-fixture")]
public sealed class AdminForceExpireTests(CheckoutTestFactory factory)
{
    [Fact]
    public async Task AdminForceExpire_WritesAuditRow()
    {
        await factory.ResetDatabaseAsync();
        var (customerToken, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-ADM-EXP", ["ksa"]);
        var warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-adm-exp", "ksa");
        await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, 5);
        await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-ADM-EXP", DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), 5);
        await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
        var cartId = await CheckoutTestSeedHelper.SeedReadyCartAsync(seedScope.ServiceProvider, accountId, "ksa", productId);

        var customerClient = factory.CreateClient();
        CheckoutCustomerAuthHelper.SetBearer(customerClient, customerToken);
        var startResp = await customerClient.PostAsJsonAsync("/v1/customer/checkout/sessions", new { cartId, marketCode = "ksa" });
        var sessionId = Guid.Parse((await startResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString()!);

        var (adminToken, _) = await CheckoutAdminAuthHelper.IssueAdminTokenAsync(factory, new[] { "checkout.read", "checkout.write" });
        var adminClient = factory.CreateClient();
        CheckoutAdminAuthHelper.SetBearer(adminClient, adminToken);

        var expireResp = await adminClient.PostAsJsonAsync($"/v1/admin/checkout/sessions/{sessionId}/expire", new { reason = "support.fixit" });
        expireResp.StatusCode.Should().Be(HttpStatusCode.OK, because: await expireResp.Content.ReadAsStringAsync());

        await using var assertScope = factory.Services.CreateAsyncScope();
        var checkoutDb = assertScope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        var session = await checkoutDb.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        session.State.Should().Be(CheckoutStates.Expired);

        // FR-015 vocabulary: `checkout.session.admin_expired` (was `checkout.admin_expired`).
        var auditDb = assertScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await auditDb.AuditLogEntries.AsNoTracking()
            .Where(a => a.Action == "checkout.session.admin_expired" && a.EntityId == sessionId).ToListAsync();
        audit.Should().ContainSingle();
    }
}
