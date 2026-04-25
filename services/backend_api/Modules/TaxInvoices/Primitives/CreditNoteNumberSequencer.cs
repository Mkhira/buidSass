using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.TaxInvoices.Primitives;

/// <summary>
/// FR-002 — credit-note numbers <c>CN-{MARKET}-{YYYYMM}-{SEQ6}</c>. Independent sequence per
/// (market, year-month) so credit-note ordering is decoupled from invoice ordering. Same
/// advisory-lock cold-path pattern as <see cref="InvoiceNumberSequencer"/>.
/// </summary>
public sealed class CreditNoteNumberSequencer(InvoicesDbContext db)
{
    private static readonly long AdvisoryLockKey = HashLockKey("invoices.credit_note_number_sequence_create");

    public async Task<string> NextAsync(string marketCode, DateTimeOffset issuedAt, CancellationToken ct)
    {
        var market = NormalizeMarket(marketCode);
        var yyyymm = issuedAt.UtcDateTime.ToString("yyyyMM", System.Globalization.CultureInfo.InvariantCulture);
        var quotedSequence = $"\"invoices\".\"cnseq_{market.ToLowerInvariant()}_{yyyymm}\"";

        long nextSeq;
        try
        {
            nextSeq = await ExecuteScalarLongAsync(db, $"SELECT nextval('{quotedSequence}')", ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
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
                await using var nextCmd = conn.CreateCommand();
                nextCmd.CommandText = $"SELECT nextval('{quotedSequence}')";
                var raw = await nextCmd.ExecuteScalarAsync(ct);
                nextSeq = Convert.ToInt64(raw, System.Globalization.CultureInfo.InvariantCulture);
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
        return $"CN-{market}-{yyyymm}-{seq6}";
    }

    private static async Task<long> ExecuteScalarLongAsync(InvoicesDbContext db, string sql, CancellationToken ct)
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
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s));
        return BitConverter.ToInt64(bytes, 0);
    }
}
