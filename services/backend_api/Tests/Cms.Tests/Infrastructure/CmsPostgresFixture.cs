using BackendApi.Modules.Cms.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Cms.Tests.Infrastructure;

/// <summary>
/// Shared Testcontainers Postgres fixture for spec 024 CMS integration tests.
/// Mirrors the pattern from <c>ReviewsPostgresFixture</c>: spins up one
/// Postgres-16-alpine container, migrates the <c>cms</c> schema, and exposes a
/// factory for fresh DbContexts. Tests truncate per-case when they need
/// isolation; the supersession-race tests use <c>NewContext()</c> twice to
/// drive concurrent connections.
/// </summary>
public sealed class CmsPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("cms_test")
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

    public CmsDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new CmsDbContext(options);
    }

    /// <summary>Truncate all CMS tables between test cases. Order matches the FK graph.</summary>
    public async Task ResetAsync()
    {
        await using var ctx = NewContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE
                cms.banner_campaign_bindings,
                cms.preview_tokens,
                cms.assets,
                cms.banner_slots,
                cms.featured_sections,
                cms.faq_entries,
                cms.blog_articles,
                cms.legal_page_versions
              RESTART IDENTITY CASCADE;");
    }
}

[CollectionDefinition(nameof(CmsPostgresCollection))]
public sealed class CmsPostgresCollection : ICollectionFixture<CmsPostgresFixture> { }
