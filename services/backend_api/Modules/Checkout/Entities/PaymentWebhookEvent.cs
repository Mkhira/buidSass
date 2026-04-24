namespace BackendApi.Modules.Checkout.Entities;

public sealed class PaymentWebhookEvent
{
    public Guid Id { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    /// <summary>Provider-supplied event id — forms a unique key with ProviderId for dedupe (R7).</summary>
    public string ProviderEventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public bool SignatureVerified { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? HandledAt { get; set; }
    public string RawPayload { get; set; } = "{}";
}
