using System.Security.Cryptography;
using System.Text;
using BackendApi.Modules.Checkout.Entities;
using BackendApi.Modules.Checkout.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Checkout.Primitives;

/// <summary>
/// DB-backed idempotency cache for POST submit (FR-007 / R3). An `Idempotency-Key` header
/// plus a sha-256 of the normalised request body forms the identity of a request. Within the
/// TTL (5 min by default), a replay with the SAME key + SAME fingerprint returns the cached
/// response; a replay with the SAME key + DIFFERENT fingerprint is a client bug and surfaces
/// as <see cref="LookupOutcome.KeyReuseWithDifferentBody"/>.
/// </summary>
public sealed class IdempotencyStore(CheckoutDbContext db, IOptions<CheckoutOptions> options)
{
    public enum LookupOutcome { Miss, Hit, KeyReuseWithDifferentBody }

    public sealed record LookupResult(LookupOutcome Outcome, IdempotencyResult? Cached);

    public async Task<LookupResult> LookupAsync(string key, string normalizedBody, CancellationToken ct)
    {
        var fingerprint = ComputeFingerprint(normalizedBody);
        var now = DateTimeOffset.UtcNow;
        var entry = await db.IdempotencyResults
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.IdempotencyKey == key && e.ExpiresAt > now, ct);
        if (entry is null)
        {
            return new LookupResult(LookupOutcome.Miss, null);
        }
        if (!entry.RequestFingerprint.AsSpan().SequenceEqual(fingerprint))
        {
            return new LookupResult(LookupOutcome.KeyReuseWithDifferentBody, null);
        }
        return new LookupResult(LookupOutcome.Hit, entry);
    }

    public async Task PersistAsync(
        string key,
        Guid? accountId,
        string normalizedBody,
        int responseStatus,
        string responseJson,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        db.IdempotencyResults.Add(new IdempotencyResult
        {
            IdempotencyKey = key,
            AccountId = accountId,
            RequestFingerprint = ComputeFingerprint(normalizedBody),
            ResponseStatus = responseStatus,
            ResponseJson = responseJson,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(options.Value.IdempotencyTtlMinutes),
        });
        await db.SaveChangesAsync(ct);
    }

    private static byte[] ComputeFingerprint(string normalizedBody)
        => SHA256.HashData(Encoding.UTF8.GetBytes(normalizedBody));
}
