using BackendApi.Modules.Orders.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Orders.Primitives;

/// <summary>
/// FR-002 + research R2. Issues human-readable order numbers <c>ORD-{MARKET}-{YYYYMM}-{SEQ6}</c>
/// using a Postgres sequence per (market, year-month). Sequences are created lazily on first
/// use so a brand-new market doesn't require a migration.
///
/// SC-002: 10 000 concurrent orders → 0 collisions. Postgres <c>nextval()</c> is process-safe
/// across connections; the only risk vector is the sequence-creation race, which we guard with
/// <c>CREATE SEQUENCE IF NOT EXISTS</c> (atomic in Postgres) inside an advisory lock so two
/// concurrent first-use calls don't both try to create the same sequence.
/// </summary>
public sealed class OrderNumberSequencer(OrdersDbContext db)
{
    private static readonly long AdvisoryLockKey = HashLockKey("orders.order_number_sequence_create");

    public async Task<string> NextAsync(string marketCode, DateTimeOffset placedAt, CancellationToken ct)
    {
        var market = NormalizeMarket(marketCode);
        var yyyymm = placedAt.UtcDateTime.ToString("yyyyMM", System.Globalization.CultureInfo.InvariantCulture);
        var sequenceName = $"orders.seq_{market.ToLowerInvariant()}_{yyyymm}";

        // Quote the qualified sequence name. The schema + suffix are derived from a constrained
        // input set (regex-validated `market`, fixed yyyymm format), so injection is impossible,
        // but Postgres won't accept parameterised identifiers in DDL/sequence functions anyway.
        var quotedSequence = $"\"orders\".\"seq_{market.ToLowerInvariant()}_{yyyymm}\"";

        long nextSeq;
        try
        {
            // Hot path: sequence already exists.
            nextSeq = await ExecuteScalarLongAsync(db, $"SELECT nextval('{quotedSequence}')", ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // undefined_table (sequence not found)
        {
            // Cold path: create sequence under advisory lock to serialise concurrent first-use.
            // CR review: do NOT `await using` the DbContext-owned connection — disposing it
            // here invalidates the DbContext for any subsequent operations on the same context.
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync(ct);
            }
            await using (var lockCmd = conn.CreateCommand())
            {
                lockCmd.CommandText = "SELECT pg_advisory_lock(@k)";
                lockCmd.Parameters.AddWithValue("k", AdvisoryLockKey);
                await lockCmd.ExecuteNonQueryAsync(ct);
            }
            try
            {
                await using (var createCmd = conn.CreateCommand())
                {
                    createCmd.CommandText =
                        $"CREATE SEQUENCE IF NOT EXISTS {quotedSequence} START 1 INCREMENT 1 MINVALUE 1 NO CYCLE";
                    await createCmd.ExecuteNonQueryAsync(ct);
                }
                await using (var nextCmd = conn.CreateCommand())
                {
                    nextCmd.CommandText = $"SELECT nextval('{quotedSequence}')";
                    var raw = await nextCmd.ExecuteScalarAsync(ct);
                    nextSeq = Convert.ToInt64(raw, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            finally
            {
                await using var unlockCmd = conn.CreateCommand();
                unlockCmd.CommandText = "SELECT pg_advisory_unlock(@k)";
                unlockCmd.Parameters.AddWithValue("k", AdvisoryLockKey);
                await unlockCmd.ExecuteNonQueryAsync(ct);
            }
        }

        var seq6 = nextSeq.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        return $"ORD-{market}-{yyyymm}-{seq6}";
    }

    private static async Task<long> ExecuteScalarLongAsync(OrdersDbContext db, string sql, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var raw = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(raw, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string NormalizeMarket(string marketCode)
    {
        if (string.IsNullOrWhiteSpace(marketCode))
        {
            throw new ArgumentException("Market code is required.", nameof(marketCode));
        }
        var trimmed = marketCode.Trim().ToUpperInvariant();
        // Constrain to alphanumeric, max 8 chars — the seq name must be a safe identifier.
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                throw new ArgumentException($"Market code '{marketCode}' contains an invalid character.", nameof(marketCode));
            }
        }
        if (trimmed.Length is 0 or > 8)
        {
            throw new ArgumentException($"Market code '{marketCode}' must be 1..8 alphanumeric chars.", nameof(marketCode));
        }
        return trimmed;
    }

    private static long HashLockKey(string s)
    {
        // Stable 64-bit hash — pg_advisory_lock takes a bigint. SHA256 first 8 bytes interpreted
        // as bigint avoids the GetHashCode randomisation across process restarts.
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s));
        return BitConverter.ToInt64(bytes, 0);
    }
}
