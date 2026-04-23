using BackendApi.Modules.Identity.Persistence;
using FluentAssertions;
using Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Integration;

public sealed class MarketCodeImmutabilityTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task ActiveAccount_MarketCodeChange_WithoutAdminScope_Throws()
    {
        await factory.ResetDatabaseAsync();
        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "market-immutability@local.dev",
            password: "StrongPassword!123");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var account = await db.Accounts.SingleAsync(x => x.Id == seed.AccountId);
        account.MarketCode = "uae";

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ActiveAccount_MarketCodeChange_WithAdminScope_Succeeds()
    {
        await factory.ResetDatabaseAsync();
        var seed = await CustomerTestDataHelper.SeedCustomerAsync(
            factory,
            email: "market-immutability-scope@local.dev",
            password: "StrongPassword!123");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var marketContext = scope.ServiceProvider.GetRequiredService<IAdminMarketChangeContext>();

        var account = await db.Accounts.SingleAsync(x => x.Id == seed.AccountId);
        account.MarketCode = "uae";

        using (marketContext.BeginScope())
        {
            await db.SaveChangesAsync();
        }

        var updated = await db.Accounts.SingleAsync(x => x.Id == seed.AccountId);
        updated.MarketCode.Should().Be("uae");
    }
}
