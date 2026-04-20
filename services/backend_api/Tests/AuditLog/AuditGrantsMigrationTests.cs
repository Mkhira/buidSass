using backend_api.Tests.Infrastructure;
using Npgsql;

namespace backend_api.Tests.AuditLog;

[Collection("PostgresCollection")]
public sealed class AuditGrantsMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task DentalApiApp_Cannot_Update_Or_Delete_AuditLogEntries()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var setRole = connection.CreateCommand();
        setRole.CommandText = "SET ROLE dental_api_app;";
        await setRole.ExecuteNonQueryAsync();

        await using var updateCmd = connection.CreateCommand();
        updateCmd.CommandText = "UPDATE audit_log_entries SET \"Reason\" = 'tampered' WHERE 1=0;";
        var updateEx = await Assert.ThrowsAsync<PostgresException>(() => updateCmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", updateEx.SqlState);

        await using var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM audit_log_entries WHERE 1=0;";
        var deleteEx = await Assert.ThrowsAsync<PostgresException>(() => deleteCmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", deleteEx.SqlState);
    }

    [Fact]
    public async Task DentalApiApp_Can_Insert_And_Select_AuditLogEntries()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var setRole = connection.CreateCommand();
        setRole.CommandText = "SET ROLE dental_api_app;";
        await setRole.ExecuteNonQueryAsync();

        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO audit_log_entries
              (""Id"", ""ActorId"", ""ActorRole"", ""Action"", ""EntityType"", ""EntityId"",
               ""BeforeState"", ""AfterState"", ""CorrelationId"", ""Reason"", ""OccurredAt"")
            VALUES
              (gen_random_uuid(), gen_random_uuid(), 'admin', 'grants.probe', 'probe', gen_random_uuid(),
               NULL, NULL, gen_random_uuid(), NULL, now());";
        await insertCmd.ExecuteNonQueryAsync();

        await using var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT COUNT(*) FROM audit_log_entries WHERE \"Action\" = 'grants.probe';";
        var count = (long)(await selectCmd.ExecuteScalarAsync() ?? 0L);
        Assert.True(count >= 1);
    }
}
