namespace BackendApi.Modules.Payments.Domain;

/// <summary>
/// Fast-path cache table for BR-3 create-payment idempotency lookups.
/// Avoids scanning the full payments table on duplicate-attempt requests.
/// <see cref="ExpiresAt"/> is set to 24h post-terminal-state for cleanup.
/// </summary>
public sealed class IdempotencyKey
{
    public string Key { get; set; } = string.Empty;
    public Guid PaymentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
}
