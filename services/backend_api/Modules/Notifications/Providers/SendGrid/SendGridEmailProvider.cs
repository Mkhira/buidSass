using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Notifications.Providers.SendGrid;

/// <summary>
/// SendGrid — backup email provider, both markets. Event-webhook payloads are
/// JSON arrays of events; signature is sent in the
/// <c>X-Twilio-Email-Event-Webhook-Signature</c> header (base64 of an ECDSA
/// signature in production; in sandbox we accept either HMAC-SHA256 fallback
/// or the envelope itself). ADR-009 v1 backup.
/// </summary>
public sealed class SendGridEmailProvider : INotificationProvider
{
    private readonly TimeProvider _clock;

    public SendGridEmailProvider(TimeProvider clock) { _clock = clock; }

    public string ProviderId => NotificationsConstants.Providers.SendGrid;
    public string Channel => NotificationsConstants.Channels.Email;
    public bool SupportsMarket(string marketCode) => NotificationsConstants.Markets.All.Contains(marketCode);

    public Task<SendResult> SendAsync(NotificationDispatch dispatch, CancellationToken cancellationToken)
    {
        var msgId = $"sg-sandbox-{dispatch.IdempotencyKey[..16]}";
        return Task.FromResult(SendResult.Ok(msgId));
    }

    public bool ValidateWebhookSignature(HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets)
    {
        vaultSecrets.TryGetValue("notifications-email/multi/sendgrid/webhook-signing-key", out var secret);
        return NotificationWebhookSignature.ValidateBase64HmacSha256FromHeader(
            request, rawBody, "X-Twilio-Email-Event-Webhook-Signature", secret);
    }

    public WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
    {
        // SendGrid event-webhook bodies are JSON arrays. We parse the first
        // event for the canonical state mapping; the audit row preserves the
        // raw bytes so multi-event payloads remain replayable.
        using var doc = JsonDocument.Parse(rawBody);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            return new WebhookEvent(ProviderId, string.Empty, "empty", CanonicalWebhookEventKinds.Unknown, _clock.GetUtcNow(), null);

        var first = doc.RootElement[0];
        var msgId = first.TryGetProperty("sg_message_id", out var mid) ? mid.GetString() ?? string.Empty : string.Empty;
        var ev = first.TryGetProperty("event", out var evEl) ? evEl.GetString() ?? "unknown" : "unknown";
        var canonical = ev switch
        {
            "delivered" => CanonicalWebhookEventKinds.Delivered,
            "bounce" => CanonicalWebhookEventKinds.Bounced,
            "dropped" => CanonicalWebhookEventKinds.Failed,
            "deferred" => CanonicalWebhookEventKinds.SoftBounced,
            "spamreport" => CanonicalWebhookEventKinds.Complaint,
            "processed" => CanonicalWebhookEventKinds.Accepted,
            _ => CanonicalWebhookEventKinds.Unknown,
        };
        var reason = first.TryGetProperty("reason", out var rEl) ? rEl.GetString() : null;
        return new WebhookEvent(ProviderId, msgId, ev, canonical, _clock.GetUtcNow(), reason);
    }
}
