namespace BackendApi.Modules.Notifications.Domain;

/// <summary>
/// Hash-stored signed token per
/// <c>data-model.md §notifications.unsubscribe_tokens</c>. The raw token is
/// HMAC-SHA256 over <c>(customer_id|channel|category|nonce|expires_at)</c> and
/// is embedded in marketing email footers (AC-21). Stored as SHA-256 of the
/// raw token; lookup happens by hashing the inbound token and matching.
/// 30-day TTL is the clarify-locked default.
/// </summary>
public sealed class UnsubscribeToken
{
    /// <summary>SHA-256 of the signed token string.</summary>
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();

    public Guid CustomerId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
