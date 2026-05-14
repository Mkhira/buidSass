using System.Text.Json;

namespace BackendApi.Modules.Shipping.Providers.Smsa;

/// <summary>
/// Maps SMSA webhook payloads to the canonical <see cref="WebhookEvent"/>
/// shape. SMSA's documented event vocabulary maps onto our internal kinds
/// as follows (per provider docs + research §3):
///
/// <c>created → label_purchased</c>, <c>picked_up → handed_to_carrier</c>,
/// <c>in_transit → in_transit</c>, <c>out_for_delivery → out_for_delivery</c>,
/// <c>delivered → delivered</c>, <c>delivery_failed → delivery_attempted</c>,
/// <c>returned → returned_to_sender</c>.
///
/// Unknown / unmapped values fall through to the raw event kind so the
/// handler can decide to log + ignore vs. surface as orphan.
/// </summary>
public static class SmsaWebhookMapper
{
    private static readonly IReadOnlyDictionary<string, string> CanonicalByProviderKind =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["created"] = CanonicalEventKinds.LabelPurchased,
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
        var trackingId = root.TryGetProperty("awb_number", out var t) ? t.GetString() ?? "" : "";
        var eventKind = root.TryGetProperty("event_kind", out var k) ? k.GetString() ?? "" : "";
        var occurredAt = root.TryGetProperty("occurred_at", out var o) && o.TryGetDateTimeOffset(out var dt)
            ? dt
            : receivedAt;
        var canonical = CanonicalByProviderKind.TryGetValue(eventKind, out var c) ? c : eventKind;

        // BR-15 — strip any recipient PII before persisting the raw payload.
        var redacted = WebhookPayloadRedactor.Redact(root);

        return new WebhookEvent(
            ProviderTrackingId: trackingId,
            ProviderEventKind: eventKind,
            CanonicalEventKind: canonical,
            OccurredAt: occurredAt,
            RawPayloadRedactedJson: redacted);
    }
}
