using System.Data;
using BackendApi.Modules.Verification.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Verification.Workers;

/// <summary>
/// Postgres advisory-lock helper for horizontal-scale worker coordination
/// (research §R12). Each worker takes a session-scoped <c>pg_try_advisory_lock</c>
/// before scanning so multiple replicas do not double-execute the daily pass.
///
/// <para>Returned <see cref="AdvisoryLockHandle"/> is an
/// <see cref="IAsyncDisposable"/>; on dispose it calls
/// <c>pg_advisory_unlock</c> and closes the underlying connection. Callers
/// MUST treat <c>handle.Acquired == false</c> as "another instance is running
/// the pass, no-op cleanly".</para>
/// </summary>
public static class PostgresAdvisoryLock
{
    /// <summary>
    /// Stable lock keys per worker. Picked at module-design time so they don't
    /// change across deploys. <c>pg_advisory_lock</c> takes a bigint; the values
    /// here fit comfortably below int max so there's no encoding ambiguity
    /// across drivers / Postgres versions.
    /// </summary>
    public static class Keys
    {
        public const long ExpiryWorker = 0x020_E1_00L;          // 2_039_040
        public const long ReminderWorker = 0x020_E2_00L;        // 2_039_296
        public const long DocumentPurgeWorker = 0x020_E3_00L;   // 2_039_552
    }

    /// <summary>
    /// Tries to acquire a Postgres session advisory lock on a fresh connection.
    /// The connection is held open for the lifetime of the returned handle so
    /// the lock isn't released until <c>DisposeAsync()</c>.
    /// </summary>
    public static async Task<AdvisoryLockHandle> TryAcquireAsync(
        VerificationDbContext dbContext,
        long key,
        CancellationToken ct)
    {
        var connectionString = dbContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException(
                "VerificationDbContext has no connection string — cannot acquire advisory lock.");

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
                // Another instance is running this worker — release our connection
                // and report no-op cleanly.
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

/// <summary>
/// Holder for an acquired Postgres advisory lock. Disposing releases it and
/// closes the connection. <see cref="Acquired"/> false means another worker
/// instance held the lock.
/// </summary>
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
            // Best-effort unlock. Connection close releases the session lock too.
        }
        finally
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
