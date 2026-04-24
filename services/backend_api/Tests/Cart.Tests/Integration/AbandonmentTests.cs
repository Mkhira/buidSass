using BackendApi.Modules.Cart.Entities;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Workers;
using BackendApi.Modules.Shared;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Integration;

/// <summary>
/// SC-004: at most one cart.abandoned event per cart per 24h dedupe window. Plant an idle cart
/// with ≥1 line (FR-010), run the worker twice, confirm a single audit row + single emission.
/// Second scenario asserts FR-010's "idle-timer reset on resume": a cart that's touched again
/// after emission earns a fresh emission once it crosses the idle threshold again.
/// </summary>
[Collection("cart-fixture")]
public sealed class AbandonmentTests(CartTestFactory factory)
{
    [Fact]
    public async Task Abandonment_EmitsOnce_PerDedupeWindow()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            await PlantIdleCart(seedScope.ServiceProvider, accountId, DateTimeOffset.UtcNow.AddHours(-2));
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var worker = ActivatorUtilities.CreateInstance<AbandonedCartWorker>(scope.ServiceProvider);
        await worker.TickAsync(CancellationToken.None);
        await worker.TickAsync(CancellationToken.None);

        await using var assertScope = factory.Services.CreateAsyncScope();
        var auditDb = assertScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cartDb = assertScope.ServiceProvider.GetRequiredService<CartDbContext>();

        var abandonments = await auditDb.AuditLogEntries.AsNoTracking()
            .Where(a => a.Action == "cart.abandoned").ToListAsync();
        abandonments.Should().ContainSingle();

        var emissions = await cartDb.Set<CartAbandonedEmission>().AsNoTracking().ToListAsync();
        emissions.Should().ContainSingle();
    }

    [Fact]
    public async Task Abandonment_EmptyCart_NoEmission()
    {
        // FR-010 requires ≥1 line; a line-less idle cart must not emit.
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<CartDbContext>();
            db.Carts.Add(new BackendApi.Modules.Cart.Entities.Cart
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                MarketCode = "ksa",
                Status = "active",
                LastTouchedAt = DateTimeOffset.UtcNow.AddHours(-2),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-3),
                UpdatedAt = DateTimeOffset.UtcNow.AddHours(-2),
                OwnerId = "platform",
            });
            await db.SaveChangesAsync();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var worker = ActivatorUtilities.CreateInstance<AbandonedCartWorker>(scope.ServiceProvider);
        await worker.TickAsync(CancellationToken.None);

        await using var assertScope = factory.Services.CreateAsyncScope();
        var auditDb = assertScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await auditDb.AuditLogEntries.AsNoTracking().Where(a => a.Action == "cart.abandoned").ToListAsync())
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Abandonment_ResumeResetsIdleTimer_ThenEmitsAgain()
    {
        // FR-010 resume-reset: after first emission, if the cart is resumed (LastTouchedAt
        // advances past LastEmittedAt), the dedupe row is cleared on the next tick. Once the
        // resumed cart goes idle again, a second emission fires — even within 24h.
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        Guid cartId;
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            cartId = await PlantIdleCart(seedScope.ServiceProvider, accountId, DateTimeOffset.UtcNow.AddHours(-2));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var worker = ActivatorUtilities.CreateInstance<AbandonedCartWorker>(scope.ServiceProvider);
            await worker.TickAsync(CancellationToken.None);
        }

        // Simulate resume: bump LastTouchedAt forward past emission time, then push back so
        // the cart is idle again.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CartDbContext>();
            var cart = await db.Carts.SingleAsync(c => c.Id == cartId);
            // Set LastTouchedAt to "just now" — worker will see this as newer than LastEmittedAt
            // and clear the emission row.
            cart.LastTouchedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var worker = ActivatorUtilities.CreateInstance<AbandonedCartWorker>(scope.ServiceProvider);
            await worker.TickAsync(CancellationToken.None);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CartDbContext>();
            (await db.Set<CartAbandonedEmission>().AsNoTracking().AnyAsync(e => e.CartId == cartId))
                .Should().BeFalse("resume cleared the dedupe row");

            // Push the cart idle again + run worker — second emission should fire.
            var cart = await db.Carts.SingleAsync(c => c.Id == cartId);
            cart.LastTouchedAt = DateTimeOffset.UtcNow.AddHours(-2);
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var worker = ActivatorUtilities.CreateInstance<AbandonedCartWorker>(scope.ServiceProvider);
            await worker.TickAsync(CancellationToken.None);
        }

        await using var assertScope = factory.Services.CreateAsyncScope();
        var auditDb = assertScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emissions = await auditDb.AuditLogEntries.AsNoTracking()
            .Where(a => a.Action == "cart.abandoned").ToListAsync();
        emissions.Should().HaveCount(2, because: "resume reset the idle timer so a second emission fired");
    }

    private static async Task<Guid> PlantIdleCart(IServiceProvider sp, Guid accountId, DateTimeOffset lastTouchedAt)
    {
        var db = sp.GetRequiredService<CartDbContext>();
        var cart = new BackendApi.Modules.Cart.Entities.Cart
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            MarketCode = "ksa",
            Status = "active",
            LastTouchedAt = lastTouchedAt,
            CreatedAt = lastTouchedAt.AddHours(-1),
            UpdatedAt = lastTouchedAt,
            OwnerId = "platform",
        };
        db.Carts.Add(cart);
        db.CartLines.Add(new CartLine
        {
            Id = Guid.NewGuid(),
            CartId = cart.Id,
            ProductId = Guid.NewGuid(),
            Qty = 1,
            AddedAt = lastTouchedAt,
            UpdatedAt = lastTouchedAt,
        });
        await db.SaveChangesAsync();
        return cart.Id;
    }
}
