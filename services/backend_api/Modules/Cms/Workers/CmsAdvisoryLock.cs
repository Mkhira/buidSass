using System.Data;
using BackendApi.Modules.Cms.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Cms.Workers;

/// <summary>
/// Postgres advisory-lock helper for horizontal-scale worker coordination per
/// research §R12. Mirrors the spec 020 pattern (Verification's
/// <c>PostgresAdvisoryLock</c>); each CMS worker takes a session-scoped
/// <c>pg_try_advisory_lock</c> before scanning so multiple replicas do not
/// double-execute the same pass.
/// </summary>
public static class CmsAdvisoryLock
{
    /// <summary>Stable lock keys per worker. Pick once at design-time.</summary>
    public static class Keys
    {
        public const long ScheduledPublishWorker = 0x024_C1_00L; // 2_278_656
        public const long AssetGcWorker = 0x024_C2_00L;          // 2_278_912
        public const long StaleDraftAlertWorker = 0x024_C3_00L;  // 2_279_168
        public const long PreviewTokenCleanupWorker = 0x024_C4_00L; // 2_279_424
    }

    public static async Task<CmsAdvisoryLockHandle> TryAcquireAsync(
        CmsDbContext dbContext,
        long key,
        CancellationToken ct)
    {
        var connectionString = dbContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException(
                "CmsDbContext has no connection string — cannot acquire advisory lock.");

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
                return new CmsAdvisoryLockHandle(null, key, acquired: false);
            }

            return new CmsAdvisoryLockHandle(connection, key, acquired: true);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

public sealed class CmsAdvisoryLockHandle : IAsyncDisposable
{
    private NpgsqlConnection? _connection;
    private readonly long _key;
    private bool _disposed;

    public bool Acquired { get; }

    internal CmsAdvisoryLockHandle(NpgsqlConnection? connection, long key, bool acquired)
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
