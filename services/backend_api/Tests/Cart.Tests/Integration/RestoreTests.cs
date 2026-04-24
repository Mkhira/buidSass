using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Cart.Persistence;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Integration;

[Collection("cart-fixture")]
public sealed class RestoreTests(CartTestFactory factory)
{
    [Fact]
    public async Task Restore_WithinRetention_Succeeds()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-RES-W", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-res-w", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 20);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-RES",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 20);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "eg");

        var (accessToken, accountId) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var client = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(client, accessToken);

        await client.PostAsJsonAsync("/v1/customer/cart/lines", new { marketCode = "ksa", productId, qty = 2 });
        await client.PostAsJsonAsync("/v1/customer/cart/switch-market",
            new { fromMarket = "ksa", toMarket = "eg" });

        await using var lookupScope = factory.Services.CreateAsyncScope();
        var db = lookupScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var archivedId = await db.Carts.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.Status == "archived")
            .Select(c => c.Id)
            .SingleAsync();

        var restoreResp = await client.PostAsJsonAsync($"/v1/customer/cart/restore/{archivedId}", new { });
        restoreResp.StatusCode.Should().Be(HttpStatusCode.OK, because: await restoreResp.Content.ReadAsStringAsync());

        var payload = await restoreResp.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("status").GetString().Should().Be("active");
        payload.GetProperty("lines").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Restore_AfterRetentionWindow_Fails()
    {
        await factory.ResetDatabaseAsync();

        var (accessToken, accountId) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        // Plant an archived cart with an ArchivedAt date beyond the configured retention window.
        // Deriving the offset from CartOptions keeps the test stable if the window is tuned.
        await using var seedScope = factory.Services.CreateAsyncScope();
        var db = seedScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var options = seedScope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackendApi.Modules.Cart.Primitives.CartOptions>>().Value;
        var expiredAt = DateTimeOffset.UtcNow.AddDays(-(options.ArchivedCartRetentionDays + 1));
        var archived = new BackendApi.Modules.Cart.Entities.Cart
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            MarketCode = "ksa",
            Status = BackendApi.Modules.Cart.Primitives.CartStatuses.Archived,
            ArchivedAt = expiredAt,
            ArchivedReason = "market_switch",
            LastTouchedAt = expiredAt,
            CreatedAt = expiredAt.AddDays(-5),
            UpdatedAt = expiredAt,
            OwnerId = "platform",
        };
        db.Carts.Add(archived);
        await db.SaveChangesAsync();

        var client = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(client, accessToken);

        var resp = await client.PostAsJsonAsync($"/v1/customer/cart/restore/{archived.Id}", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("cart.restore.expired");
    }
}
