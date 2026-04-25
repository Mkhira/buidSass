namespace BackendApi.Modules.Checkout.Entities;

/// <summary>
/// Cached response for an idempotent POST .../submit. Keyed by the `Idempotency-Key` header
/// per R3. `RequestFingerprint` is a sha-256 of the normalised request body — a collision on
/// key with a different body is a client bug (the response is NOT replayed in that case).
/// </summary>
public sealed class IdempotencyResult
{
    public string IdempotencyKey { get; set; } = string.Empty;
    /// <summary>
    /// Composite-PK component. Submit requires auth (FR-019) so this is always set; scoping
    /// by (AccountId, IdempotencyKey) prevents two different accounts from colliding on the
    /// same caller-generated key — CR review on PR #30 round 2.
    /// </summary>
    public Guid AccountId { get; set; }
    public byte[] RequestFingerprint { get; set; } = Array.Empty<byte>();
    public int ResponseStatus { get; set; }
    public string ResponseJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
}
