namespace BackendApi.Modules.Shipping.Domain;

/// <summary>
/// Idempotency index for provider webhooks per <c>data-model.md</c>
/// §webhooks_received. Composite PK
/// <c>(provider_id, provider_tracking_id, event_kind, occurred_at)</c>
/// makes duplicate webhook delivery a no-op (BR-6 / AC-11).
/// </summary>
public sealed class WebhookReceived
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderTrackingId { get; set; } = string.Empty;
    public string EventKind { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public bool SignatureValidated { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
