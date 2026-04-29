using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Verification.Tests.Integration;

/// <summary>
/// Spec 020 tasks T043. Verifies the Postgres
/// <c>verification_state_transitions_append_only_trg</c> trigger raises on every
/// UPDATE / DELETE attempt — protecting the audit-faithful ledger from drift
/// even if a future code path tries to mutate a transition row.
/// </summary>
public sealed class StateTransitionAppendOnlyTriggerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_trigger_test")
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

    private VerificationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<VerificationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new VerificationDbContext(options);
    }

    [Fact]
    public async Task Update_on_state_transition_raises_via_trigger()
    {
        var transitionId = await GetSeededTransitionIdAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            UPDATE verification.verification_state_transitions
            SET ""Reason"" = 'tampered'
            WHERE ""Id"" = '{transitionId}';";

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = (await act.Should().ThrowAsync<PostgresException>()).Which;
        ex.SqlState.Should().Be("23000",
            "the append-only trigger MUST raise SQLSTATE 23000 on UPDATE");
        ex.MessageText.Should().Contain("verification_state_transitions is append-only",
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
            DELETE FROM verification.verification_state_transitions
            WHERE ""Id"" = '{transitionId}';";

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = (await act.Should().ThrowAsync<PostgresException>()).Which;
        ex.SqlState.Should().Be("23000",
            "the append-only trigger MUST raise SQLSTATE 23000 on DELETE");
    }

    [Fact]
    public async Task Insert_on_state_transition_succeeds_normally()
    {
        // Ensure the trigger does NOT block legitimate INSERTs — append-only must
        // still allow APPEND.
        await using var ctx = NewContext();
        var verificationId = await ctx.Verifications.Select(v => v.Id).FirstAsync();

        ctx.StateTransitions.Add(new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = verificationId,
            MarketCode = "ksa",
            PriorState = VerificationStateMachine.PriorStateNoneWire,
            NewState = "submitted",
            ActorKind = "customer",
            Reason = "additional_test_row",
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

        // Seed the v1 KSA market schema so the verification's FK to
        // (MarketCode, SchemaVersion) resolves.
        ctx.MarketSchemas.Add(new VerificationMarketSchema
        {
            MarketCode = "ksa",
            Version = 1,
            EffectiveFrom = DateTimeOffset.UtcNow,
            RetentionMonths = 24,
            CooldownDays = 7,
            ExpiryDays = 365,
            SlaDecisionBusinessDays = 2,
            SlaWarningBusinessDays = 1,
        });

        var verificationId = Guid.NewGuid();
        ctx.Verifications.Add(new BackendApi.Modules.Verification.Entities.Verification
        {
            Id = verificationId,
            CustomerId = Guid.NewGuid(),
            MarketCode = "ksa",
            SchemaVersion = 1,
            Profession = "dentist",
            RegulatorIdentifier = "SCFHS-1234567",
            State = VerificationState.Submitted,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        ctx.StateTransitions.Add(new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = verificationId,
            MarketCode = "ksa",
            PriorState = VerificationStateMachine.PriorStateNoneWire,
            NewState = "submitted",
            ActorKind = "customer",
            Reason = "initial_submission",
            MetadataJson = "{}",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await ctx.SaveChangesAsync();
    }

    private async Task<Guid> GetSeededTransitionIdAsync()
    {
        await using var ctx = NewContext();
        return await ctx.StateTransitions
            .Where(t => t.Reason == "initial_submission")
            .Select(t => t.Id)
            .FirstAsync();
    }
}
