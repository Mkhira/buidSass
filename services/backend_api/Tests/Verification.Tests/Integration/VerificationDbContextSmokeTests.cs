using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Verification.Tests.Integration;

/// <summary>
/// Spec 020 tasks T042. Spins up Testcontainers Postgres, applies the
/// <see cref="VerificationDbContext"/> migration, runs
/// <see cref="VerificationReferenceDataSeeder"/>, and asserts:
/// <list type="bullet">
///   <item>seeder is idempotent (two runs produce two rows, not four),</item>
///   <item>both KSA and EG version-1 rows exist with the V1 defaults,</item>
///   <item>the unique-active partial index rejects a second active row per market.</item>
/// </list>
/// </summary>
public sealed class VerificationDbContextSmokeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_smoke_test")
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
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private VerificationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<VerificationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new VerificationDbContext(options);
    }

    [Fact]
    public async Task Migration_creates_six_tables_in_verification_schema()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'verification'
            ORDER BY table_name;";

        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        tables.Should().BeEquivalentTo(new[]
        {
            "verification_documents",
            "verification_eligibility_cache",
            "verification_market_schemas",
            "verification_reminders",
            "verification_state_transitions",
            "verifications",
        });
    }

    [Fact]
    public async Task Reference_seeder_inserts_KSA_and_EG_v1_rows()
    {
        await RunSeederAsync();

        await using var ctx = NewContext();
        var rows = await ctx.MarketSchemas.OrderBy(s => s.MarketCode).ToListAsync();

        rows.Should().HaveCount(2);

        var eg = rows.Single(s => s.MarketCode == "eg");
        eg.Version.Should().Be(1);
        eg.RetentionMonths.Should().Be(36);
        eg.CooldownDays.Should().Be(7);
        eg.ExpiryDays.Should().Be(365);
        eg.SlaDecisionBusinessDays.Should().Be(2);
        eg.SlaWarningBusinessDays.Should().Be(1);
        eg.EffectiveTo.Should().BeNull("the v1 row MUST be the currently-active schema");

        var ksa = rows.Single(s => s.MarketCode == "ksa");
        ksa.Version.Should().Be(1);
        ksa.RetentionMonths.Should().Be(24);
        ksa.EffectiveTo.Should().BeNull();
    }

    [Fact]
    public async Task Reference_seeder_is_idempotent()
    {
        await RunSeederAsync();
        await RunSeederAsync();

        await using var ctx = NewContext();
        var count = await ctx.MarketSchemas.CountAsync();

        count.Should().Be(2, "running the seeder twice MUST NOT duplicate the per-market rows");
    }

    [Fact]
    public async Task Unique_active_partial_index_rejects_second_active_per_market()
    {
        await RunSeederAsync();

        // Try to INSERT a second row for KSA with EffectiveTo = NULL — should hit
        // UX_verification_market_schemas_active_per_market and raise a unique-violation.
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO verification.verification_market_schemas
                (""MarketCode"", ""Version"", ""EffectiveFrom"", ""EffectiveTo"",
                 ""RequiredFields"", ""AllowedDocumentTypes"", ""RetentionMonths"",
                 ""CooldownDays"", ""ExpiryDays"", ""ReminderWindowsDays"",
                 ""SlaDecisionBusinessDays"", ""SlaWarningBusinessDays"", ""HolidaysList"")
            VALUES
                ('ksa', 99, now(), NULL,
                 '[]'::jsonb, '[]'::jsonb, 24,
                 7, 365, '[30,14,7,1]'::jsonb,
                 2, 1, '[]'::jsonb);";

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = (await act.Should().ThrowAsync<PostgresException>()).Which;
        ex.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation,
            "a second EffectiveTo IS NULL row per market MUST violate UX_verification_market_schemas_active_per_market");
    }

    [Fact]
    public async Task Superseded_schema_does_not_block_a_new_active_row()
    {
        await RunSeederAsync();

        // Mark KSA v1 as superseded, then INSERT a v2 active row — should succeed.
        await using var ctx = NewContext();
        var ksaV1 = await ctx.MarketSchemas
            .SingleAsync(s => s.MarketCode == "ksa" && s.Version == 1);
        ksaV1.EffectiveTo = DateTimeOffset.UtcNow;

        ctx.MarketSchemas.Add(new BackendApi.Modules.Verification.Entities.VerificationMarketSchema
        {
            MarketCode = "ksa",
            Version = 2,
            EffectiveFrom = DateTimeOffset.UtcNow,
            EffectiveTo = null,
            RequiredFieldsJson = "[]",
            RetentionMonths = 24,
            CooldownDays = 7,
            ExpiryDays = 365,
            SlaDecisionBusinessDays = 2,
            SlaWarningBusinessDays = 1,
        });

        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().NotThrowAsync(
            "succeeding the prior version with EffectiveTo set MUST allow a fresh active row");
    }

    private async Task RunSeederAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<VerificationDbContext>(options =>
            options.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();

        var seeder = new VerificationReferenceDataSeeder();
        var ctx = new SeedContext(
            Db: null!, // The platform AppDbContext isn't needed by this module seeder.
            Services: provider,
            Size: DatasetSize.Small,
            Env: new TestHostEnv(),
            Logger: NullLogger.Instance);

        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private sealed class TestHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Verification.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
