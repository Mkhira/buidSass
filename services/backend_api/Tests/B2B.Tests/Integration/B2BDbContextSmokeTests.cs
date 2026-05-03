using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Seeding;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 T050 — boots a Testcontainers Postgres, applies migrations, runs the
/// reference-data seeder, and asserts the desired state:
/// <list type="bullet">
///   <item>2 <c>quote_market_schemas</c> rows: KSA v1 + EG v1.</item>
///   <item>The unique-active partial index rejects a second active row per market.</item>
///   <item>Re-running the seeder is a no-op.</item>
/// </list>
/// </summary>
public sealed class B2BDbContextSmokeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("b2b_smoke")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private B2BDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<B2BDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new B2BDbContext(options);
    }

    [Fact]
    public async Task Seeder_inserts_two_rows_one_per_market()
    {
        await RunSeederAsync();

        await using var ctx = NewContext();
        var rows = await ctx.QuoteMarketSchemas.OrderBy(s => s.MarketCode).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.MarketCode).Should().BeEquivalentTo(new[] { "eg", "ksa" });
        rows.Should().OnlyContain(r => r.Version == 1);
        rows.Should().OnlyContain(r => r.EffectiveTo == null, "v1 rows are currently active");
        rows.Should().OnlyContain(r => r.ValidityDays == 14);
        rows.Should().OnlyContain(r => r.CompanyVerificationRequired == false,
            "Clarifications Q2 — V1 default OFF for both markets");
        rows.Should().OnlyContain(r => r.InvitationTtlDays == 14);
    }

    [Fact]
    public async Task Seeder_is_idempotent_across_repeated_runs()
    {
        await RunSeederAsync();
        await RunSeederAsync();
        await RunSeederAsync();

        await using var ctx = NewContext();
        var count = await ctx.QuoteMarketSchemas.CountAsync();
        count.Should().Be(2, "re-running the seeder MUST converge to exactly 2 rows");
    }

    [Fact]
    public async Task Unique_active_per_market_index_rejects_a_second_active_row()
    {
        await RunSeederAsync();

        await using var ctx = NewContext();
        ctx.QuoteMarketSchemas.Add(new QuoteMarketSchema
        {
            MarketCode = "ksa",
            Version = 2,
            EffectiveFrom = DateTimeOffset.UtcNow,
            EffectiveTo = null, // <-- the unique-active partial index forbids this
            ValidityDays = 14,
            RateLimitPerCustomerPerHour = 10,
            RateLimitPerCompanyPerHour = 50,
            CompanyVerificationRequired = false,
            TaxPreviewDriftThresholdPct = 5.00m,
            SlaDecisionBusinessDays = 2,
            SlaWarningBusinessDays = 1,
            InvitationTtlDays = 14,
            HolidaysListJson = "[]",
        });

        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "UX_quote_market_schemas_active_per_market is UNIQUE on (market_code) WHERE effective_to IS NULL");
    }

    [Fact]
    public async Task A_second_versioned_row_with_effective_to_set_is_allowed()
    {
        await RunSeederAsync();

        // Close v1 and insert v2 as the new active row — the supported migration path.
        await using var ctx = NewContext();
        var v1 = await ctx.QuoteMarketSchemas.SingleAsync(s => s.MarketCode == "ksa" && s.Version == 1);
        v1.EffectiveTo = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync();

        ctx.QuoteMarketSchemas.Add(new QuoteMarketSchema
        {
            MarketCode = "ksa",
            Version = 2,
            EffectiveFrom = DateTimeOffset.UtcNow,
            EffectiveTo = null,
            ValidityDays = 14,
            RateLimitPerCustomerPerHour = 10,
            RateLimitPerCompanyPerHour = 50,
            CompanyVerificationRequired = false,
            TaxPreviewDriftThresholdPct = 5.00m,
            SlaDecisionBusinessDays = 2,
            SlaWarningBusinessDays = 1,
            InvitationTtlDays = 14,
            HolidaysListJson = "[]",
        });

        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().NotThrowAsync(
            "with v1 closed (effective_to non-null), a v2 active row MUST be insertable");
    }

    private async Task RunSeederAsync()
    {
        // Use a real DI scope so the seeder resolves B2BDbContext exactly the way the
        // production CLI verb does. The empty AppDbContext is required by the SeedContext
        // shape but never read by the B2B reference-data seeder.
        var services = new ServiceCollection();
        services.AddDbContext<B2BDbContext>(opts => opts.UseNpgsql(_postgres.GetConnectionString()));
        services.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(_postgres.GetConnectionString()));
        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();

        var seeder = new B2BReferenceDataSeeder();
        var ctx = new SeedContext(
            Db: scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            Services: scope.ServiceProvider,
            Size: DatasetSize.Small,
            Env: new HostingEnvironment(),
            Logger: NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private sealed class HostingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "B2B.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
