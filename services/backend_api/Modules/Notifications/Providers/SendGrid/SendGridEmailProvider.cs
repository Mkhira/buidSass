using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Notifications.Providers.SendGrid;

/// <summary>
/// SendGrid — backup email provider, both markets. Event-webhook payloads are
/// JSON arrays of events. ADR-009 v1 backup.
///
/// <para><strong>Sandbox-only signature validation.</strong></para>
/// Production SendGrid Event Webhooks sign each payload with ECDSA over the
/// secp256r1 curve (key derived from the operator-configured verification
/// secret). The signature lives in <c>X-Twilio-Email-Event-Webhook-Signature</c>
/// (base64-DER) and the request-time-of-signing in
/// <c>X-Twilio-Email-Event-Webhook-Timestamp</c>; the canonical string is
/// <c>timestamp + raw_body</c>. Implementing ECDSA verification against a
/// stored verification public key lands when T011 KV creds are populated.
/// <para>
/// Until then, this validator accepts only HMAC-SHA256 (base64) carried in
/// the same header — that lets fixture-driven tests exercise the dispatch
/// path without standing up a real SendGrid account. Production deployment
/// MUST swap this for the ECDSA verifier before the webhook ingress is
/// opened to the public internet.
/// </para>
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
