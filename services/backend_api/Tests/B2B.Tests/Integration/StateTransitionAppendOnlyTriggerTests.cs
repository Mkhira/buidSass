using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 T051 — verifies the Postgres trigger
/// <c>quote_state_transitions_append_only</c> raises on every UPDATE / DELETE attempt
/// and that INSERT is unaffected. The trigger function name + message are part of the
/// ops triage contract — keep them stable.
/// </summary>
public sealed class StateTransitionAppendOnlyTriggerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("b2b_append_only")
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
    public async Task Update_on_state_transition_raises_via_trigger()
    {
        var transitionId = await GetSeededTransitionIdAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            UPDATE b2b.quote_state_transitions
            SET new_state = 'rejected'
            WHERE ""Id"" = '{transitionId}';";

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = (await act.Should().ThrowAsync<PostgresException>()).Which;
        ex.SqlState.Should().Be("23514",
            "the append-only trigger MUST raise SQLSTATE 23514 (check_violation) on UPDATE");
        ex.MessageText.Should().Contain("quote_state_transitions is append-only",
            "the trigger message identifies the violating table for ops triage");
    }

    [Fact]
    public async Task Delete_on_state_transition_raises_via_trigger()
    {
        var transitionId = await GetSeededTransitionIdAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            DELETE FROM b2b.quote_state_transitions
            WHERE ""Id"" = '{transitionId}';";

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = (await act.Should().ThrowAsync<PostgresException>()).Which;
        ex.SqlState.Should().Be("23514",
            "the append-only trigger MUST raise SQLSTATE 23514 on DELETE");
    }

    [Fact]
    public async Task Insert_on_state_transition_succeeds()
    {
        // Append-only must still allow APPEND — insert a second transition row and
        // confirm the trigger does not reject it.
        await using var ctx = NewContext();
        var quoteId = await ctx.Quotes.Select(q => q.Id).FirstAsync();
        ctx.QuoteStateTransitions.Add(new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = quoteId,
            MarketCode = "ksa",
            PriorState = "requested",
            NewState = "drafted",
            ActorKind = "admin_operator",
            ActorId = Guid.NewGuid(),
            ReasonJson = null,
            MetadataJson = "{}",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().NotThrowAsync(
            "INSERT MUST succeed — only UPDATE and DELETE are forbidden by the append-only trigger");
    }

    private async Task SeedRowsAsync()
    {
        await using var ctx = NewContext();

        // Seed a market schema first so quote.schema_version FK resolves.
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
            CompanyId = null,
            MarketCode = "ksa",
            State = "requested",
            RequestedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            RestrictionPolicySnapshotJson = "{}",
        });

        ctx.QuoteStateTransitions.Add(new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = quoteId,
            MarketCode = "ksa",
            PriorState = "__none__",
            NewState = "requested",
            ActorKind = "customer",
            ActorId = Guid.NewGuid(),
            ReasonJson = null,
            MetadataJson = "{\"seed\":\"initial\"}",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await ctx.SaveChangesAsync();
    }

    private async Task<Guid> GetSeededTransitionIdAsync()
    {
        // jsonb columns don't support `LIKE` (which EF translates from string.Contains).
        // The seed inserts exactly one transition with PriorState='__none__' — use that.
        await using var ctx = NewContext();
        return await ctx.QuoteStateTransitions
            .Where(t => t.PriorState == "__none__")
            .Select(t => t.Id)
            .FirstAsync();
    }
}
