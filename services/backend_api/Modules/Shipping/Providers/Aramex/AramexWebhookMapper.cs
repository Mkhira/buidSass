using System.Text.Json;

namespace BackendApi.Modules.Shipping.Providers.Aramex;

/// <summary>Maps Aramex (KSA + EG) webhook events to the canonical kinds.</summary>
public static class AramexWebhookMapper
{
    private static readonly IReadOnlyDictionary<string, string> CanonicalByProviderKind =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["shipment_created"] = CanonicalEventKinds.LabelPurchased,
            ["picked_up"] = CanonicalEventKinds.HandedToCarrier,
            ["in_transit"] = CanonicalEventKinds.InTransit,
            ["out_for_delivery"] = CanonicalEventKinds.OutForDelivery,
            ["delivered"] = CanonicalEventKinds.Delivered,
            ["attempt_failed"] = CanonicalEventKinds.DeliveryAttempted,
            ["returned_to_sender"] = CanonicalEventKinds.ReturnedToSender,
        };

    public static WebhookEvent Parse(byte[] rawBody, DateTimeOffset receivedAt)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var trackingId = root.TryGetProperty("waybill_number", out var t) ? t.GetString() ?? "" : "";
        var eventKind = root.TryGetProperty("event", out var k) ? k.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(trackingId))
        {
            throw new InvalidOperationException("Aramex webhook missing waybill_number.");
        }
        if (string.IsNullOrWhiteSpace(eventKind))
        {
            throw new InvalidOperationException("Aramex webhook missing event.");
        }
        var occurredAt = root.TryGetProperty("event_time", out var o) && o.TryGetDateTimeOffset(out var dt)
            ? dt
            : receivedAt;
        var canonical = CanonicalByProviderKind.TryGetValue(eventKind, out var c) ? c : eventKind;
        var redacted = WebhookPayloadRedactor.Redact(root);

        return new WebhookEvent(trackingId, eventKind, canonical, occurredAt, redacted);
    }
}
