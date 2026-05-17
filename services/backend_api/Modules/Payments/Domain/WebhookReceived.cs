namespace BackendApi.Modules.Payments.Domain;

/// <summary>
/// BR-4 webhook-idempotency row. Composite PK on
/// <c>(ProviderId, ProviderMessageId, EventKind)</c> guarantees no double-processing
/// even when providers retry deliveries.
/// </summary>
public sealed class WebhookReceived
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderMessageId { get; set; } = string.Empty;
    public string EventKind { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool SignatureValidated { get; set; }

    /// <summary>SHA-256 of the raw body; for manual debug only — never used for auth.</summary>
    public string? BodyHash { get; set; }
}
