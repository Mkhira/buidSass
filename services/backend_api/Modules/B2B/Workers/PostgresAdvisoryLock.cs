using System.Data;
using BackendApi.Modules.B2B.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.B2B.Workers;

/// <summary>
/// Postgres advisory-lock helper for horizontal-scale worker coordination
/// (research §R7). Each B2B worker takes a session-scoped <c>pg_try_advisory_lock</c>
/// before scanning so multiple replicas do not double-execute the daily pass.
/// Mirrors <c>Modules/Verification/Workers/PostgresAdvisoryLock</c>.
/// </summary>
public static class PostgresAdvisoryLock
{
    public static class Keys
    {
        public const long QuoteExpiryWorker = 0x021_E1_00L;       // 2_039_808
        public const long InvitationExpiryWorker = 0x021_E2_00L;  // 2_040_064
    }

    public static async Task<AdvisoryLockHandle> TryAcquireAsync(
        B2BDbContext dbContext,
        long key,
        CancellationToken ct)
    {
        var connectionString = dbContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException(
                "B2BDbContext has no connection string — cannot acquire advisory lock.");

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_lock(@key);";
            cmd.Parameters.Add(new NpgsqlParameter("key", key));
            var result = await cmd.ExecuteScalarAsync(ct);
            var acquired = result is bool b && b;

            if (!acquired)
            {
                await connection.DisposeAsync();
                return new AdvisoryLockHandle(null, key, acquired: false);
            }

            return new AdvisoryLockHandle(connection, key, acquired: true);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

public sealed class AdvisoryLockHandle : IAsyncDisposable
{
    private NpgsqlConnection? _connection;
    private readonly long _key;
    private bool _disposed;

    public bool Acquired { get; }

    internal AdvisoryLockHandle(NpgsqlConnection? connection, long key, bool acquired)
    {
        _connection = connection;
        _key = key;
        Acquired = acquired;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_connection is null) return;

        try
        {
            if (_connection.State == ConnectionState.Open)
            {
                await using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT pg_advisory_unlock(@key);";
                cmd.Parameters.Add(new NpgsqlParameter("key", _key));
                await cmd.ExecuteScalarAsync();
            }
        }
        catch
        {
            // Best-effort unlock; closing the connection releases the session lock too.
        }
        finally
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
