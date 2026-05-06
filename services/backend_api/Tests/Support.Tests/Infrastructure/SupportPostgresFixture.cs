using BackendApi.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Support.Tests.Infrastructure;

/// <summary>
/// Shared Testcontainers Postgres fixture per spec 023 task T051. Migrates
/// the <c>support</c> schema once at startup; tests are responsible for
/// truncating between cases when they need isolation.
/// </summary>
public sealed class SupportPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("support_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public SupportDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new SupportDbContext(options);
    }
}

[CollectionDefinition(nameof(SupportPostgresCollection))]
public sealed class SupportPostgresCollection : ICollectionFixture<SupportPostgresFixture> { }
