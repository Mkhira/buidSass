using System.Collections;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;

namespace BackendApi.Modules.Identity.Primitives;

public sealed class RefreshTokenRevocationStore : ITokenRevocationCache, IRefreshTokenRevocationStore
{
    private readonly ILogger<RefreshTokenRevocationStore> _logger;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ConcurrentDictionary<byte[], byte> _snapshot = new(ByteArrayComparer.Instance);
    private readonly BloomFilter _bloomFilter = new(2_000_000);
    private readonly Lock _bloomLock = new();

    public RefreshTokenRevocationStore(
        NpgsqlDataSource dataSource,
        ILogger<RefreshTokenRevocationStore> logger)
    {
        _logger = logger;
        _dataSource = dataSource;
    }

    public bool MightContain(ReadOnlySpan<byte> tokenHash)
    {
        lock (_bloomLock)
        {
            return _bloomFilter.MightContain(tokenHash);
        }
    }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        var replacement = new HashSet<byte[]>(ByteArrayComparer.Instance);
        const string sql = """
            SELECT "TokenHash"
            FROM identity.revoked_refresh_tokens
            WHERE "RevokedAt" >= now() - interval '90 days';
            """;

        try
        {
            await using var command = _dataSource.CreateCommand(sql);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var hash = (byte[])reader["TokenHash"];
                replacement.Add(hash);
            }

            _snapshot.Clear();

            lock (_bloomLock)
            {
                _bloomFilter.Reset();

                foreach (var hash in replacement)
                {
                    _snapshot.TryAdd(hash, 0);
                    _bloomFilter.Add(hash);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to refresh refresh-token revocation cache.");
        }
    }

    public async Task RevokeAsync(
        byte[] tokenHash,
        string reason,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        const string insertSql = """
            INSERT INTO identity.revoked_refresh_tokens ("TokenHash", "RevokedAt", "Reason", "ActorId")
            VALUES (@tokenHash, now(), @reason, @actorId)
            ON CONFLICT ("TokenHash") DO NOTHING;
            """;

        var retryPolicy = BuildRetryPolicy();
        await retryPolicy.ExecuteAsync(async ct =>
        {
            await using var command = _dataSource.CreateCommand(insertSql);
            command.Parameters.AddWithValue("tokenHash", tokenHash);
            command.Parameters.AddWithValue("reason", reason);
            command.Parameters.AddWithValue("actorId", actorId is null ? DBNull.Value : actorId.Value);
            await command.ExecuteNonQueryAsync(ct);
            var persisted = await IsPersistedAsync(tokenHash, ct);
            if (!persisted)
            {
                throw new InvalidOperationException("Revoked refresh token persistence check failed.");
            }
        }, cancellationToken);

        _snapshot.TryAdd(tokenHash, 0);
        lock (_bloomLock)
        {
            _bloomFilter.Add(tokenHash);
        }
    }

    public async Task RevokeBySessionAsync(
        Guid sessionId,
        string reason,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var hashes = await LoadSessionTokenHashesAsync(sessionId, cancellationToken);
        if (hashes.Count == 0)
        {
            return;
        }

        const string insertSql = """
            INSERT INTO identity.revoked_refresh_tokens ("TokenHash", "RevokedAt", "Reason", "ActorId")
            VALUES (@tokenHash, now(), @reason, @actorId)
            ON CONFLICT ("TokenHash") DO NOTHING;
            """;

        var retryPolicy = BuildRetryPolicy();
        await retryPolicy.ExecuteAsync(async ct =>
        {
            foreach (var tokenHash in hashes)
            {
                await using var command = _dataSource.CreateCommand(insertSql);
                command.Parameters.AddWithValue("tokenHash", tokenHash);
                command.Parameters.AddWithValue("reason", reason);
                command.Parameters.AddWithValue("actorId", actorId is null ? DBNull.Value : actorId.Value);
                await command.ExecuteNonQueryAsync(ct);
            }

            foreach (var tokenHash in hashes)
            {
                var persisted = await IsPersistedAsync(tokenHash, ct);
                if (!persisted)
                {
                    throw new InvalidOperationException("Session revocation persistence check failed.");
                }
            }
        }, cancellationToken);

        foreach (var tokenHash in hashes)
        {
            _snapshot.TryAdd(tokenHash, 0);
        }

        lock (_bloomLock)
        {
            foreach (var tokenHash in hashes)
            {
                _bloomFilter.Add(tokenHash);
            }
        }
    }

    public async Task<bool> IsRevokedAsync(byte[] tokenHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        if (!MightContain(tokenHash))
        {
            return false;
        }

        const string sql = """
            SELECT 1
            FROM identity.revoked_refresh_tokens
            WHERE "TokenHash" = @tokenHash
            LIMIT 1;
            """;

        try
        {
            await using var command = _dataSource.CreateCommand(sql);
            command.Parameters.AddWithValue("tokenHash", tokenHash);
            var scalar = await command.ExecuteScalarAsync(cancellationToken);
            return scalar is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Revocation lookup failed; treating token as revoked.");
            return true;
        }
    }

    private async Task<bool> IsPersistedAsync(byte[] tokenHash, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1
            FROM identity.revoked_refresh_tokens
            WHERE "TokenHash" = @tokenHash
            LIMIT 1;
            """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("tokenHash", tokenHash);
        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is not null;
    }

    private async Task<List<byte[]>> LoadSessionTokenHashesAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE("TokenSecretHash", "TokenHash") AS token_hash
            FROM identity.refresh_tokens
            WHERE "SessionId" = @sessionId
              AND COALESCE("TokenSecretHash", "TokenHash") IS NOT NULL;
            """;

        var hashes = new HashSet<byte[]>(ByteArrayComparer.Instance);
        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("sessionId", sessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader["token_hash"] is byte[] hash)
            {
                hashes.Add(hash);
            }
        }

        return [.. hashes];
    }

    private AsyncPolicy BuildRetryPolicy()
    {
        return Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(80 * attempt + Random.Shared.Next(0, 60)),
                onRetry: (exception, delay, attempt, _) =>
                {
                    _logger.LogWarning(
                        exception,
                        "Retrying refresh token revocation persistence. attempt={Attempt} delayMs={DelayMs}",
                        attempt,
                        delay.TotalMilliseconds);
                });
    }

    private sealed class BloomFilter
    {
        private readonly BitArray _bits;
        private readonly int _size;

        public BloomFilter(int size)
        {
            _size = size;
            _bits = new BitArray(size);
        }

        public void Add(byte[] value)
        {
            foreach (var index in GetIndexes(value))
            {
                _bits[index] = true;
            }
        }

        public bool MightContain(ReadOnlySpan<byte> value)
        {
            foreach (var index in GetIndexes(value.ToArray()))
            {
                if (!_bits[index])
                {
                    return false;
                }
            }

            return true;
        }

        public void Reset() => _bits.SetAll(false);

        private IEnumerable<int> GetIndexes(byte[] value)
        {
            var hashA = SHA256.HashData(value);
            var hashB = SHA1.HashData(value);

            yield return (int)(BitConverter.ToUInt32(hashA, 0) % (uint)_size);
            yield return (int)(BitConverter.ToUInt32(hashA, 8) % (uint)_size);
            yield return (int)(BitConverter.ToUInt32(hashB, 0) % (uint)_size);
        }
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        public bool Equals(byte[]? x, byte[]? y)
        {
            if (x is null || y is null)
            {
                return false;
            }

            return x.AsSpan().SequenceEqual(y);
        }

        public int GetHashCode(byte[] obj)
        {
            return BitConverter.ToInt32(SHA256.HashData(obj), 0);
        }
    }
}
