using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Shared;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Contract.Admin;

[Collection("cart-fixture")]
public sealed class GetCartContractTests(CartTestFactory factory)
{
    [Fact]
    public async Task AdminGetCart_ReturnsCartAndWritesAuditRow()
    {
        await factory.ResetDatabaseAsync();

        // Create a customer cart via the public surface.
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-ADM-001", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-adm-1", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 5);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-ADM",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 5);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var customerClient = factory.CreateClient();
        await customerClient.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 1 });

        Guid cartId;
        await using (var lookupScope = factory.Services.CreateAsyncScope())
        {
            var cartDb = lookupScope.ServiceProvider.GetRequiredService<CartDbContext>();
            cartId = await cartDb.Carts.AsNoTracking().Select(c => c.Id).SingleAsync();
        }

        var (adminToken, _) = await CartAdminAuthHelper.IssueAdminTokenAsync(factory, ["cart.admin.read"]);
        var adminClient = factory.CreateClient();
        CartAdminAuthHelper.SetBearer(adminClient, adminToken);

        var resp = await adminClient.GetAsync($"/v1/admin/cart/carts/{cartId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK, because: await resp.Content.ReadAsStringAsync());

        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("lines").GetArrayLength().Should().Be(1);

        // Audit row verifies Principle 25 coverage.
        await using var assertScope = factory.Services.CreateAsyncScope();
        var auditDb = assertScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await auditDb.AuditLogEntries.AsNoTracking()
            .Where(a => a.Action == "cart.admin_viewed")
            .ToListAsync();
        audit.Should().ContainSingle();
        audit[0].EntityId.Should().Be(cartId);
    }
}
