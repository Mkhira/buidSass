using BackendApi.Modules.Pricing.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Persistence;

/// <summary>
/// Spec 007-b T047 — confirms the 007-b commercial-authoring migration applied
/// cleanly via the shared <see cref="PricingTestFactory"/> (which calls
/// <c>Database.MigrateAsync()</c> at startup) and that the resulting schema
/// matches the data-model §2 contract.
///
/// This test is intentionally read-only against pg_catalog so it can run
/// alongside other tests without resetting the DB.
/// </summary>
[Collection("pricing-fixture")]
public sealed class MigrationApplicationTests(PricingTestFactory factory)
{
    [Theory]
    [InlineData("commercial_thresholds")]
    [InlineData("commercial_approvals")]
    [InlineData("commercial_audit_events")]
    [InlineData("campaigns")]
    [InlineData("campaign_links")]
    [InlineData("preview_profiles")]
    public async Task Commercial_Authoring_Tables_Exist_In_Pricing_Schema(string tableName)
    {
        var exists = await TableExists("pricing", tableName);
        exists.Should().BeTrue($"migration must create pricing.{tableName} (data-model §2)");
    }

    [Fact]
    public async Task Commercial_Audit_Events_Trigger_Is_Wired()
    {
        // Spec 007-b T047 / data-model §2.9 — append-only enforcement via
        // raise_immutable_audit_violation() trigger.
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM pg_trigger t
            JOIN pg_class c ON c.oid = t.tgrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_proc p ON p.oid = t.tgfoid
            WHERE n.nspname = 'pricing'
              AND c.relname = 'commercial_audit_events'
              AND NOT t.tgisinternal
              AND t.tgname = 'trg_commercial_audit_events_immutable'
              AND p.proname = 'raise_immutable_audit_violation';";

        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(1, "the immutable trigger must be wired to the expected function (CodeRabbit nit)");
    }

    [Fact]
    public async Task Commercial_Thresholds_Has_MarketCode_Primary_Key()
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        // Verify the PK is on the expected column (MarketCode), not just that
        // some PK exists (CodeRabbit nit). The schema uses the project's
        // PascalCase EF mapping, so we lower-case the result for a stable
        // comparison.
        cmd.CommandText = @"
            SELECT array_agg(lower(a.attname) ORDER BY u.ord)
            FROM pg_constraint c
            JOIN pg_class r ON r.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = r.relnamespace
            JOIN LATERAL unnest(c.conkey) WITH ORDINALITY u(attnum, ord) ON true
            JOIN pg_attribute a ON a.attrelid = r.oid AND a.attnum = u.attnum
            WHERE n.nspname = 'pricing'
              AND r.relname = 'commercial_thresholds'
              AND c.contype = 'p'
            GROUP BY c.oid;";

        var pkColumns = (string[])(await cmd.ExecuteScalarAsync())!;
        pkColumns.Should().BeEquivalentTo(
            new[] { "marketcode" },
            opts => opts.WithStrictOrdering(),
            "PK must be on the MarketCode column");
    }

    [Fact]
    public async Task Pricing_DbContext_Resolves_All_Commercial_DbSets()
    {
        // Sanity: smoke-asserts T023 — DbContext registers the 6 new DbSet<>s.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();

        // Each query is no-op-safe because of the empty truncated DB / fresh migration;
        // success means EF model + DbSet are wired.
        await db.Campaigns.AsNoTracking().Take(0).ToListAsync();
        await db.CampaignLinks.AsNoTracking().Take(0).ToListAsync();
        await db.PreviewProfiles.AsNoTracking().Take(0).ToListAsync();
        await db.CommercialThresholds.AsNoTracking().Take(0).ToListAsync();
        await db.CommercialApprovals.AsNoTracking().Take(0).ToListAsync();
        await db.CommercialAuditEvents.AsNoTracking().Take(0).ToListAsync();
    }

    private async Task<bool> TableExists(string schema, string table)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT EXISTS (
                SELECT 1 FROM pg_tables
                WHERE schemaname = @s AND tablename = @t
            );";
        cmd.Parameters.AddWithValue("s", schema);
        cmd.Parameters.AddWithValue("t", table);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }
}
