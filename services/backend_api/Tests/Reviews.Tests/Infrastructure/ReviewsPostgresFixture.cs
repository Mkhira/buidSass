using BackendApi.Modules.Reviews.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Reviews.Tests.Infrastructure;

/// <summary>
/// Spec 022 T037 substitute — shared Testcontainers Postgres fixture so
/// foundational integration tests don't each spin up their own container.
/// Migrates the reviews schema once at startup; tests are responsible for
/// truncating between cases when they need isolation.
/// </summary>
public sealed class ReviewsPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("reviews_test")
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

    public ReviewsDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ReviewsDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new ReviewsDbContext(options);
    }
}

[CollectionDefinition(nameof(ReviewsPostgresCollection))]
public sealed class ReviewsPostgresCollection : ICollectionFixture<ReviewsPostgresFixture> { }
