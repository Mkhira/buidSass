using BackendApi.Modules.Reviews.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Reviews.Workers;

/// <summary>
/// Postgres advisory-lock helper specific to the Reviews module workers — same
/// pattern as VerificationModule's <c>PostgresAdvisoryLock</c>. Each worker
/// takes a session-scoped <c>pg_try_advisory_lock</c> before scanning so
/// multiple replicas do not double-execute the daily pass.
///
/// <para>Lock keys are picked at module-design time and pinned here so they
/// don't drift across deploys. Failure to acquire is treated as "another
/// instance is running this worker — no-op cleanly".</para>
/// </summary>
public static class ReviewsAdvisoryLock
{
    public static class Keys
    {
        // Hash-stable picks; values fit comfortably below int max so the
        // bigint encoding is unambiguous across Npgsql versions.
        public const long RatingAggregateRebuild = 0x022_F1_00L; // 36_245_248
        public const long ReviewIntegrityScan = 0x022_F2_00L; // 36_245_504
    }

    public static async Task<AdvisoryLockHandle> TryAcquireAsync(
        ReviewsDbContext db,
        long key,
        CancellationToken ct,
        NpgsqlDataSource? dataSource = null)
    {
        // EF Core 9: when the DbContext is configured via UseNpgsql(NpgsqlDataSource)
        // — which is the production wiring — GetConnectionString() returns null.
        // Prefer the registered NpgsqlDataSource (handles pooling + auth correctly);
        // only fall back to building a fresh NpgsqlConnection from a string when
        // the DbContext was configured via UseNpgsql(string) (e.g. integration tests).
        NpgsqlConnection connection;
        if (dataSource is not null)
        {
            connection = await dataSource.OpenConnectionAsync(ct);
        }
        else
        {
            var connectionString = db.Database.GetConnectionString()
                ?? throw new InvalidOperationException(
                    "ReviewsAdvisoryLock requires either an NpgsqlDataSource or a connection string. " +
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
/// Holder for an acquired Postgres advisory lock. Disposing releases the lock
/// + closes the connection. <see cref="Acquired"/> false means another
/// instance held the lock; the worker must short-circuit cleanly.
/// </summary>
public sealed class AdvisoryLockHandle : IAsyncDisposable
{
    private readonly NpgsqlConnection? _connection;
    private readonly long _key;

    public AdvisoryLockHandle(NpgsqlConnection? connection, long key, bool acquired)
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
