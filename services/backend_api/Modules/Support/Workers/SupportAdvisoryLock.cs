using BackendApi.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Support.Workers;

/// <summary>
/// Postgres advisory-lock helper for the Support module's hosted workers
/// (mirrors <see cref="BackendApi.Modules.Reviews.Workers.ReviewsAdvisoryLock"/>).
/// Each worker takes a session-scoped <c>pg_try_advisory_lock</c> before
/// scanning so multiple replicas do not double-execute the same pass.
///
/// <para>Lock keys are pinned at module-design time so they do not drift
/// across deploys. Failure to acquire is treated as "another instance is
/// running this worker" and the worker no-ops cleanly.</para>
/// </summary>
public static class SupportAdvisoryLock
{
    public static class Keys
    {
        // Hash-stable picks under int.MaxValue so the bigint encoding is
        // unambiguous. Numbered with the spec prefix (023) for traceability.
        public const long SlaBreachWatch = 0x023_F1_00L; // 36_311_040
        public const long AutoCloseResolutionWindow = 0x023_F2_00L; // 36_311_296
        public const long OrphanedAssignmentReclaim = 0x023_F3_00L; // 36_311_552
    }

    public static async Task<SupportAdvisoryLockHandle> TryAcquireAsync(
        SupportDbContext db,
        long key,
        CancellationToken ct,
        NpgsqlDataSource? dataSource = null)
    {
        // EF Core 9: when DbContext is configured via UseNpgsql(NpgsqlDataSource)
        // — production wiring — GetConnectionString() returns null. Prefer the
        // registered NpgsqlDataSource (handles pooling + auth correctly); only
        // fall back to a fresh NpgsqlConnection from a string when DbContext
        // was configured via UseNpgsql(string) (e.g. integration tests).
        NpgsqlConnection connection;
        if (dataSource is not null)
        {
            connection = await dataSource.OpenConnectionAsync(ct);
        }
        else
        {
            var connectionString = db.Database.GetConnectionString()
                ?? throw new InvalidOperationException(
                    "SupportAdvisoryLock requires either an NpgsqlDataSource or a connection string. " +
                    "When the DbContext is configured via UseNpgsql(NpgsqlDataSource), GetConnectionString() " +
                    "returns null and the caller must pass the registered NpgsqlDataSource explicitly.");
            connection = new NpgsqlConnection(connectionString);
        }

        try
        {
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
            }

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_lock(@key);";
            cmd.Parameters.Add(new NpgsqlParameter("key", key));
            var result = await cmd.ExecuteScalarAsync(ct);
            var acquired = result is bool b && b;

            if (!acquired)
            {
                await connection.DisposeAsync();
                return new SupportAdvisoryLockHandle(null, key, acquired: false);
            }
            return new SupportAdvisoryLockHandle(connection, key, acquired: true);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

/// <summary>
/// Handle to an acquired Postgres advisory lock. Disposing releases the lock
/// + closes the connection. <see cref="Acquired"/> false means another instance
/// held the lock; the worker must short-circuit cleanly.
/// </summary>
public sealed class SupportAdvisoryLockHandle : IAsyncDisposable
{
    private readonly NpgsqlConnection? _connection;
    private readonly long _key;

    public SupportAdvisoryLockHandle(NpgsqlConnection? connection, long key, bool acquired)
    {
        _connection = connection;
        _key = key;
        Acquired = acquired;
    }

    public bool Acquired { get; }

    public async ValueTask DisposeAsync()
    {
        if (!Acquired || _connection is null) return;

        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT pg_advisory_unlock(@key);";
            cmd.Parameters.Add(new NpgsqlParameter("key", _key));
            await cmd.ExecuteScalarAsync();
        }
        catch
        {
            // Best-effort unlock — connection close releases the session lock anyway.
        }
        finally
        {
            await _connection.DisposeAsync();
        }
    }
}
