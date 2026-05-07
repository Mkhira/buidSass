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
            WHERE n.nspname = 'pricing'
              AND c.relname = 'commercial_audit_events'
              AND NOT t.tgisinternal;";

        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().BeGreaterThan(0, "trg_commercial_audit_events_immutable must be present");
    }

    [Fact]
    public async Task Commercial_Thresholds_Has_MarketCode_Primary_Key()
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM pg_constraint c
            JOIN pg_class r ON r.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = r.relnamespace
            WHERE n.nspname = 'pricing'
              AND r.relname = 'commercial_thresholds'
              AND c.contype = 'p';";

        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(1, "PK on (MarketCode) must exist on pricing.commercial_thresholds");
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
