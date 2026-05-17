namespace BackendApi.Modules.Notifications.Domain;

/// <summary>
/// Idempotency surface for provider webhook deliveries per
/// <c>data-model.md §notifications.webhooks_received</c>. Composite PK
/// <c>(provider_id, provider_message_id, event_kind)</c> enforces V-6
/// (duplicate webhook is ignored — return 200 OK regardless).
/// </summary>
public sealed class WebhookReceived
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderMessageId { get; set; } = string.Empty;
    public string EventKind { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool SignatureValidated { get; set; }
}
