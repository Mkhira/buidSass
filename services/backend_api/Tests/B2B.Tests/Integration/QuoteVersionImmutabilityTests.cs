using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 T052 — verifies <c>quote_versions</c> rows are row-level immutable. An EF
/// UPDATE MUST be rejected by the Postgres trigger
/// <c>quote_versions_immutable</c>. INSERT and cascade DELETE (via parent) remain valid.
/// </summary>
public sealed class QuoteVersionImmutabilityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("b2b_qv_immutable")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();
        await SeedRowsAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private B2BDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<B2BDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new B2BDbContext(options);
    }

    [Fact]
    public async Task Direct_sql_update_on_quote_version_raises_via_trigger()
    {
        var versionId = await GetSeededVersionIdAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            UPDATE b2b.quote_versions
            SET terms_days = 90
            WHERE ""Id"" = '{versionId}';";

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = (await act.Should().ThrowAsync<PostgresException>()).Which;
        ex.SqlState.Should().Be("23514",
            "the immutability trigger MUST raise SQLSTATE 23514 on UPDATE");
        ex.MessageText.Should().Contain("quote_versions is immutable",
            "the trigger message identifies the violating table for ops triage");
    }

    [Fact]
    public async Task Insert_succeeds_normally()
    {
        await using var ctx = NewContext();
        var quoteId = await ctx.Quotes.Select(q => q.Id).FirstAsync();

        ctx.QuoteVersions.Add(new QuoteVersion
        {
            Id = Guid.NewGuid(),
            QuoteId = quoteId,
            VersionNumber = 2,
            AuthoredBy = Guid.NewGuid(),
            PublishedAt = DateTimeOffset.UtcNow,
            LineItemsJson = "[]",
            TermsTextJson = "{\"en\":\"Net 30\",\"ar\":\"صافي 30\"}",
            TermsDays = 30,
            ValidityExtends = false,
            TotalsSummaryJson = "{\"subtotal\":0,\"total_discount\":0,\"total_tax_preview\":0,\"grand_total\":0,\"currency\":\"SAR\"}",
        });

        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().NotThrowAsync("INSERT MUST succeed — only UPDATE is forbidden");
    }

    private async Task SeedRowsAsync()
    {
        await using var ctx = NewContext();

        ctx.QuoteMarketSchemas.Add(new QuoteMarketSchema
        {
            MarketCode = "ksa",
            Version = 1,
            EffectiveFrom = DateTimeOffset.UtcNow,
        });

        var quoteId = Guid.NewGuid();
        ctx.Quotes.Add(new Quote
        {
            Id = quoteId,
            CustomerId = Guid.NewGuid(),
            MarketCode = "ksa",
            State = "revised",
            RequestedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            RestrictionPolicySnapshotJson = "{}",
        });

        ctx.QuoteVersions.Add(new QuoteVersion
        {
            Id = Guid.NewGuid(),
            QuoteId = quoteId,
            VersionNumber = 1,
            AuthoredBy = Guid.NewGuid(),
            PublishedAt = DateTimeOffset.UtcNow,
            LineItemsJson = "[]",
            TermsTextJson = "{\"en\":\"Net 14\",\"ar\":\"صافي 14\"}",
            TermsDays = 14,
            ValidityExtends = false,
            TotalsSummaryJson = "{\"subtotal\":0,\"total_discount\":0,\"total_tax_preview\":0,\"grand_total\":0,\"currency\":\"SAR\"}",
        });

        await ctx.SaveChangesAsync();
    }

    private async Task<Guid> GetSeededVersionIdAsync()
    {
        await using var ctx = NewContext();
        return await ctx.QuoteVersions.Select(v => v.Id).FirstAsync();
    }
}
