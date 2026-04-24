using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Integration;

/// <summary>
/// C3 / FR-003: customer sign-in triggers CartLoginMergeHook. Start with an anon cart, POST
/// /v1/customer/identity/sign-in with the cart_token in the cookie, assert the auth cart
/// exists with the anon line adopted.
/// </summary>
[Collection("cart-fixture")]
public sealed class LoginMergeHookTests(CartTestFactory factory)
{
    [Fact]
    public async Task SignIn_WithAnonCartToken_AdoptsAnonCart()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-LOGIN", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-login", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 20);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-LOGIN",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 20);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        // 1. Create an identity account with a known password so we can POST sign-in.
        var email = $"login-merge-{Guid.NewGuid():N}@example.test";
        var password = "LoginMergeTest!123";
        Guid accountId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var hasher = scope.ServiceProvider.GetRequiredService<Argon2idHasher>();
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var account = new Account
            {
                Id = Guid.NewGuid(),
                Surface = "customer",
                MarketCode = "ksa",
                EmailNormalized = email.ToLowerInvariant(),
                EmailDisplay = email,
                PasswordHash = hasher.HashPassword(password, SurfaceKind.Customer),
                PasswordHashVersion = 1,
                PermissionVersion = 1,
                Status = "active",
                EmailVerifiedAt = DateTimeOffset.UtcNow,
                Locale = "en",
                DisplayName = "Login Merge Tester",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            identityDb.Accounts.Add(account);
            await identityDb.SaveChangesAsync();
            accountId = account.Id;
        }

        // 2. Anon cart: add 2 of productId.
        var anonClient = factory.CreateClient();
        var addResp = await anonClient.PostAsJsonAsync("/v1/customer/cart/lines",
            new { marketCode = "ksa", productId, qty = 2 });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cartToken = addResp.Headers.GetValues("X-Cart-Token").Single();

        // 3. Sign in with the anon token in the header — hook fires.
        var signInClient = factory.CreateClient();
        signInClient.DefaultRequestHeaders.Add("X-Cart-Token", cartToken);
        var signInResp = await signInClient.PostAsJsonAsync("/v1/customer/identity/sign-in", new
        {
            identifier = email,
            password,
        });
        signInResp.StatusCode.Should().Be(HttpStatusCode.OK, because: await signInResp.Content.ReadAsStringAsync());

        // 4. Assert: the auth cart exists + has the anon line (qty=2).
        await using var assertScope = factory.Services.CreateAsyncScope();
        var cartDb = assertScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var authCart = await cartDb.Carts.AsNoTracking()
            .SingleAsync(c => c.AccountId == accountId && c.Status == "active");
        var lines = await cartDb.CartLines.AsNoTracking().Where(l => l.CartId == authCart.Id).ToListAsync();
        lines.Should().ContainSingle();
        lines[0].ProductId.Should().Be(productId);
        lines[0].Qty.Should().Be(2);

        // Anon cart should be in `merged` state.
        var mergedAnon = await cartDb.Carts.AsNoTracking()
            .Where(c => c.AccountId == null && c.CartTokenHash != null).ToListAsync();
        mergedAnon.Should().ContainSingle();
        mergedAnon[0].Status.Should().Be("merged");
    }
}
