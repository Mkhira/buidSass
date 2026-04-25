using System.Security.Cryptography;
using System.Text;
using BackendApi.Modules.Checkout.Entities;
using BackendApi.Modules.Checkout.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BackendApi.Modules.Checkout.Primitives;

/// <summary>
/// DB-backed idempotency cache for POST submit (FR-007 / R3). An `Idempotency-Key` header
/// plus a sha-256 of the normalised request body forms the identity of a request. Within the
/// TTL (5 min by default):
///   - same key + same fingerprint + completed → cached response replayed (`Hit`).
///   - same key + same fingerprint + still claimed → `InProgress` (client must retry).
///   - same key + different fingerprint → `KeyReuseWithDifferentBody` (client bug).
///
/// CR review on PR #30: the previous read-then-insert flow was racy — two concurrent first-use
/// submits could both miss `LookupAsync`, both run the full submit pipeline, and then one would
/// 23505 on persist and 500. <see cref="TryClaimAsync"/> is the new atomic entry point: it
/// inserts a placeholder row (ResponseStatus = 0) under a unique key, so only one caller wins
/// and the rest see the placeholder. The winner runs the pipeline and calls
/// <see cref="PersistAsync"/> which UPDATEs the placeholder with the real response.
/// </summary>
public sealed class IdempotencyStore(CheckoutDbContext db, IOptions<CheckoutOptions> options)
{
    /// <summary>Sentinel response status for "claim placeholder, no response yet".</summary>
    private const int PendingResponseStatus = 0;

    public enum ClaimOutcome
    {
        /// <summary>Caller owns the work; must call <see cref="PersistAsync"/> to record the response.</summary>
        Claimed,
        /// <summary>An earlier replay completed; the cached response is in <see cref="ClaimResult.Cached"/>.</summary>
        Hit,
        /// <summary>Another in-flight request owns this key; client should retry shortly.</summary>
        InProgress,
        /// <summary>Same key, different request body — client bug; surface 422.</summary>
        KeyReuseWithDifferentBody,
    }

    public sealed record ClaimResult(ClaimOutcome Outcome, IdempotencyResult? Cached);

    /// <summary>
    /// Atomically claim the idempotency key. INSERTs a placeholder row keyed on `IdempotencyKey`;
    /// on 23505 the loser re-reads the existing row and returns the appropriate outcome.
    /// </summary>
    public async Task<ClaimResult> TryClaimAsync(
        string key,
        Guid accountId,
        string normalizedBody,
        CancellationToken ct)
    {
        var fingerprint = ComputeFingerprint(normalizedBody);
        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromMinutes(options.Value.IdempotencyTtlMinutes);
        var placeholder = new IdempotencyResult
        {
            IdempotencyKey = key,
            AccountId = accountId,
            RequestFingerprint = fingerprint,
            ResponseStatus = PendingResponseStatus,
            ResponseJson = "{}",
            CreatedAt = now,
            ExpiresAt = now + ttl,
        };
        db.IdempotencyResults.Add(placeholder);
        try
        {
            await db.SaveChangesAsync(ct);
            return new ClaimResult(ClaimOutcome.Claimed, null);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(placeholder).State = EntityState.Detached;
        }

        // Lost the race — re-read the existing row scoped to (AccountId, Key) so a different
        // account can't ever read or mutate this caller's claim.
        var existing = await db.IdempotencyResults.AsNoTracking()
            .SingleOrDefaultAsync(e => e.AccountId == accountId && e.IdempotencyKey == key, ct);
        if (existing is null)
        {
            // Extremely narrow window: the conflicting row was deleted (TTL purge) between the
            // failed insert and our re-read. Treat as a fresh claim and retry once.
            db.IdempotencyResults.Add(placeholder);
            try
            {
                await db.SaveChangesAsync(ct);
                return new ClaimResult(ClaimOutcome.Claimed, null);
            }
            catch (DbUpdateException ex2) when (IsUniqueViolation(ex2))
            {
                db.Entry(placeholder).State = EntityState.Detached;
                return new ClaimResult(ClaimOutcome.InProgress, null);
            }
        }

        if (existing.ExpiresAt <= now)
        {
            // The row was expired; another caller's tick will GC it. Surface InProgress so the
            // client retries — by then the cache should be clean. Avoiding silent expiry-skip
            // keeps us from accidentally running the pipeline twice for a still-cached key.
            return new ClaimResult(ClaimOutcome.InProgress, null);
        }
        if (!existing.RequestFingerprint.AsSpan().SequenceEqual(fingerprint))
        {
            return new ClaimResult(ClaimOutcome.KeyReuseWithDifferentBody, null);
        }
        if (existing.ResponseStatus == PendingResponseStatus)
        {
            return new ClaimResult(ClaimOutcome.InProgress, null);
        }
        return new ClaimResult(ClaimOutcome.Hit, existing);
    }

    /// <summary>
    /// Update the row claimed earlier with the actual response. Idempotent — if no claim exists
    /// (claim TTL expired before persist) the row is upserted so the response is still cached.
    /// </summary>
    public async Task PersistAsync(
        string key,
        Guid accountId,
        string normalizedBody,
        int responseStatus,
        string responseJson,
        CancellationToken ct)
    {
        var fingerprint = ComputeFingerprint(normalizedBody);
        var now = DateTimeOffset.UtcNow;
        var rows = await db.IdempotencyResults
            .Where(e => e.AccountId == accountId && e.IdempotencyKey == key)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.ResponseStatus, responseStatus)
                .SetProperty(e => e.ResponseJson, responseJson)
                .SetProperty(e => e.RequestFingerprint, fingerprint)
                .SetProperty(e => e.ExpiresAt, now.AddMinutes(options.Value.IdempotencyTtlMinutes)), ct);
        if (rows == 0)
        {
            // No claim row to update (claim got TTL-purged or never existed). Insert directly so
            // the cached response is still available for a subsequent replay.
            db.IdempotencyResults.Add(new IdempotencyResult
            {
                IdempotencyKey = key,
                AccountId = accountId,
                RequestFingerprint = fingerprint,
                ResponseStatus = responseStatus,
                ResponseJson = responseJson,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(options.Value.IdempotencyTtlMinutes),
            });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Concurrent insert raced us — accept it; their persist already cached the answer.
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Release a claim that the caller was unable to complete (e.g., handler threw mid-flight).</summary>
    public async Task ReleaseClaimAsync(string key, Guid accountId, CancellationToken ct)
    {
        await db.IdempotencyResults
            .Where(e => e.AccountId == accountId && e.IdempotencyKey == key && e.ResponseStatus == PendingResponseStatus)
            .ExecuteDeleteAsync(ct);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException pg && pg.SqlState == "23505";

    private static byte[] ComputeFingerprint(string normalizedBody)
        => SHA256.HashData(Encoding.UTF8.GetBytes(normalizedBody));
}
