using System.Text.Json;

namespace BackendApi.Modules.Shipping.Providers.Bosta;

/// <summary>Maps Bosta webhook events to the canonical kinds.</summary>
public static class BostaWebhookMapper
{
    private static readonly IReadOnlyDictionary<string, string> CanonicalByProviderKind =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pickup_requested"] = CanonicalEventKinds.LabelPurchased,
            ["picked_up"] = CanonicalEventKinds.HandedToCarrier,
            ["in_transit"] = CanonicalEventKinds.InTransit,
            ["out_for_delivery"] = CanonicalEventKinds.OutForDelivery,
            ["delivered"] = CanonicalEventKinds.Delivered,
            ["delivery_failed"] = CanonicalEventKinds.DeliveryAttempted,
            ["returned"] = CanonicalEventKinds.ReturnedToSender,
        };

    public static WebhookEvent Parse(byte[] rawBody, DateTimeOffset receivedAt)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var trackingId = root.TryGetProperty("tracking_number", out var t) ? t.GetString() ?? "" : "";
        var eventKind = root.TryGetProperty("state", out var k) ? k.GetString() ?? "" : "";
        var occurredAt = root.TryGetProperty("timestamp", out var o) && o.TryGetDateTimeOffset(out var dt)
            ? dt
            : receivedAt;
        var canonical = CanonicalByProviderKind.TryGetValue(eventKind, out var c) ? c : eventKind;
        var redacted = WebhookPayloadRedactor.Redact(root);
        return new WebhookEvent(trackingId, eventKind, canonical, occurredAt, redacted);
    }
}
