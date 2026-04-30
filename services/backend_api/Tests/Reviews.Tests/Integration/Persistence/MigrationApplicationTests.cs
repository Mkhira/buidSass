using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reviews.Tests.Infrastructure;

namespace Reviews.Tests.Integration.Persistence;

/// <summary>
/// Spec 022 T050 — applies <c>CreateReviewsSchemaAndTables</c> against
/// Testcontainers Postgres; introspects via <c>pg_catalog</c> to assert the
/// schema, all 7 tables, the unique-partial index, and the 3 append-only
/// triggers landed.
/// </summary>
[Collection(nameof(ReviewsPostgresCollection))]
public sealed class MigrationApplicationTests
{
    private readonly ReviewsPostgresFixture _fx;

    public MigrationApplicationTests(ReviewsPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Reviews_schema_exists_with_seven_tables()
    {
        var tables = await QueryListAsync(@"
SELECT table_name FROM information_schema.tables
WHERE table_schema = 'reviews' ORDER BY table_name;");
        tables.Should().BeEquivalentTo(new[]
        {
            "product_rating_aggregates",
            "review_admin_notes",
            "review_flags",
            "review_moderation_decisions",
            "reviews",
            "reviews_filter_wordlists",
            "reviews_market_schemas",
        }, options => options.WithoutStrictOrdering());
    }

    [Fact]
    public async Task Unique_partial_index_on_reviews_customer_product_active_exists()
    {
        var indexes = await QueryListAsync(@"
SELECT indexname FROM pg_indexes
WHERE schemaname = 'reviews' AND tablename = 'reviews';");
        indexes.Should().Contain("UX_reviews_customer_product_active");
    }

    [Fact]
    public async Task Append_only_triggers_exist_on_three_audit_tables()
    {
        var triggers = await QueryListAsync(@"
SELECT trigger_name FROM information_schema.triggers
WHERE trigger_schema = 'reviews'
ORDER BY trigger_name;");
        triggers.Should().Contain(new[]
        {
            "review_admin_notes_append_only_trg",
            "review_flags_append_only_trg",
            "review_moderation_decisions_append_only_trg",
        });
    }

    [Fact]
    public async Task Raise_immutable_audit_violation_function_is_namespaced_to_reviews()
    {
        var functions = await QueryListAsync(@"
SELECT p.proname FROM pg_proc p
JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname = 'reviews' AND p.proname = 'raise_immutable_audit_violation';");
        functions.Should().HaveCount(1, "the audit-violation trigger function MUST live in the reviews schema (per migration to avoid colliding with sibling modules)");
    }

    private async Task<IReadOnlyList<string>> QueryListAsync(string sql)
    {
        await using var ctx = _fx.NewContext();
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                rows.Add(reader.GetString(0));
            }
            return rows;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
