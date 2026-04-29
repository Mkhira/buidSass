using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Verification.Tests.Integration;

/// <summary>
/// Spec 020 task T099. Verifies the partial unique index
/// <c>UX_verification_market_schemas_active_per_market</c>
/// rejects a second <c>effective_to IS NULL</c> row for the same market —
/// keeping the "one active per market" invariant safe at the DB level.
/// </summary>
public sealed class MarketSchemaActiveConstraintTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_active_constraint_test")
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

    private VerificationDbContext NewContext() => new(
        new DbContextOptionsBuilder<VerificationDbContext>().UseNpgsql(ConnectionString).Options);

    [Fact]
    public async Task Two_active_rows_for_same_market_rejected_by_partial_unique_index()
    {
        await using var ctx = NewContext();
        ctx.MarketSchemas.Add(BuildSchema("ksa", 1));
        await ctx.SaveChangesAsync();

        await using var ctx2 = NewContext();
        ctx2.MarketSchemas.Add(BuildSchema("ksa", 2));

        var act = async () => await ctx2.SaveChangesAsync();
        var ex = await act.Should().ThrowAsync<DbUpdateException>();
        ex.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation,
                "two effective rows for the same market must violate UX_verification_market_schemas_active_per_market");
    }

    [Fact]
    public async Task Active_row_per_market_can_coexist_across_markets()
    {
        await using var ctx = NewContext();
        ctx.MarketSchemas.Add(BuildSchema("ksa", 1));
        ctx.MarketSchemas.Add(BuildSchema("eg", 1));

        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().NotThrowAsync(
            "the partial unique index is per-market, not global");
    }

    [Fact]
    public async Task Retiring_v1_then_inserting_v2_is_allowed()
    {
        var publishAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

        await using var ctx = NewContext();
        ctx.MarketSchemas.Add(BuildSchema("ksa", 1));
        await ctx.SaveChangesAsync();

        await using var ctx2 = NewContext();
        var v1 = await ctx2.MarketSchemas.SingleAsync(s => s.MarketCode == "ksa" && s.Version == 1);
        v1.EffectiveTo = publishAt;
        ctx2.MarketSchemas.Add(BuildSchema("ksa", 2));

        var act = async () => await ctx2.SaveChangesAsync();
        await act.Should().NotThrowAsync(
            "marking v1 effective_to=now and inserting v2 in the same Tx is the supported promotion path");
    }

    private static VerificationMarketSchema BuildSchema(string marketCode, int version) => new()
    {
        MarketCode = marketCode,
        Version = version,
        EffectiveFrom = DateTimeOffset.UtcNow,
        EffectiveTo = null,
        RequiredFieldsJson = "[]",
        RetentionMonths = 24,
        CooldownDays = 7,
        ExpiryDays = 365,
    };
}
