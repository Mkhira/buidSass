using FluentAssertions;
using Npgsql;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Persistence;

/// <summary>
/// Spec 007-b T048 / data-model §2.9 — confirms the immutable audit-event trigger
/// raises on UPDATE and DELETE attempts against pricing.commercial_audit_events.
///
/// Audit events are append-only by contract; the trigger
/// pricing.raise_immutable_audit_violation() enforces this at the database layer
/// regardless of the calling code path. This test bypasses EF entirely and
/// exercises raw SQL so the guarantee is verified at the storage tier.
/// </summary>
[Collection("pricing-fixture")]
public sealed class CommercialAuditEventAppendOnlyTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Update_On_Commercial_Audit_Event_Raises_Trigger_Error()
    {
        var auditId = await SeedAuditRowAsync();

        var act = async () =>
        {
            await using var connection = new NpgsqlConnection(factory.ConnectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE pricing.commercial_audit_events
                                   SET ""ReasonNote"" = 'tampered'
                                 WHERE ""Id"" = @id;";
            cmd.Parameters.AddWithValue("id", auditId);
            await cmd.ExecuteNonQueryAsync();
        };

        // SQLSTATE P0001 == raise_exception (the trigger function uses
        // ERRCODE='P0001' explicitly). Anchor on SQLSTATE and the literal
        // 'append-only' phrase from the trigger message so an unrelated DB
        // error cannot make this test pass (CodeRabbit nit).
        await act.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == "P0001"
                      && ex.MessageText.Contains("append-only", StringComparison.OrdinalIgnoreCase));

        await CleanupAuditRowAsync(auditId);
    }

    [Fact]
    public async Task Delete_On_Commercial_Audit_Event_Raises_Trigger_Error()
    {
        var auditId = await SeedAuditRowAsync();

        var act = async () =>
        {
            await using var connection = new NpgsqlConnection(factory.ConnectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"DELETE FROM pricing.commercial_audit_events WHERE ""Id"" = @id;";
            cmd.Parameters.AddWithValue("id", auditId);
            await cmd.ExecuteNonQueryAsync();
        };

        // SQLSTATE P0001 == raise_exception (the trigger function uses
        // ERRCODE='P0001' explicitly). Anchor on SQLSTATE and the literal
        // 'append-only' phrase from the trigger message so an unrelated DB
        // error cannot make this test pass (CodeRabbit nit).
        await act.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == "P0001"
                      && ex.MessageText.Contains("append-only", StringComparison.OrdinalIgnoreCase));

        // Cleanup unreachable — the row CANNOT be deleted by design. We let the
        // test DB carry the seeded row; subsequent tests do not depend on it.
    }

    private async Task<Guid> SeedAuditRowAsync()
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO pricing.commercial_audit_events
                (""Id"", ""TargetEntityKind"", ""TargetEntityId"", ""Kind"",
                 ""ActorId"", ""ActorRole"",
                 ""BeforeJson"", ""AfterJson"", ""DiffJson"", ""ReasonNote"",
                 ""RecordedAtUtc"", ""CorrelationId"")
            VALUES
                (@id, 'coupon', @target, 'coupon.created',
                 '00000000-0000-0000-0000-000000000007', 'system',
                 NULL, '{}'::jsonb, NULL, 'commercial test seed',
                 NOW(), NULL);";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("target", Guid.NewGuid());
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task CleanupAuditRowAsync(Guid id)
    {
        // Defensive cleanup attempt — guaranteed to fail because of the trigger,
        // but we run it inside a try/catch so the test method's "expected
        // throw" assertion remains the only verifier of immutability. We swallow
        // the resulting exception so leftover rows accumulate harmlessly across
        // shard runs (the test DB is recreated per CI run).
        try
        {
            await using var connection = new NpgsqlConnection(factory.ConnectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"DELETE FROM pricing.commercial_audit_events WHERE ""Id"" = @id;";
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            // expected — the immutable-trigger error from
            // raise_immutable_audit_violation() (ERRCODE='P0001'). Any other
            // PostgresException must propagate so the test fails on real DB
            // problems (CodeRabbit Minor, loop 2).
        }
    }
}
