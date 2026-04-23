using BackendApi.Modules.Search.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Search.Tests.Infrastructure;

namespace Search.Tests.Integration;

[Collection("search-fixture")]
public sealed class SearchTestFactorySmokeTests(SearchTestFactory factory)
{
    [Fact]
    public async Task MigrateAsync_CompletesWithoutPendingModelChangesWarning()
    {
        await factory.ResetDatabaseAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SearchDbContext>();
        var pending = await dbContext.Database.GetPendingMigrationsAsync();

        pending.Should().BeEmpty();
    }
}
