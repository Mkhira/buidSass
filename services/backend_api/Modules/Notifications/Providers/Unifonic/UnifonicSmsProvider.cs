using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Notifications.Providers.Unifonic;

/// <summary>
/// Unifonic — primary KSA SMS provider (covers OTP + transactional + marketing).
/// Webhooks deliver DLRs (Delivery Receipts) HMAC-SHA256 signed in
/// <c>X-Unifonic-Signature</c>. ADR-009 v1 stack.
/// </summary>
public sealed class UnifonicSmsProvider : INotificationProvider
{
    private readonly TimeProvider _clock;

    public UnifonicSmsProvider(TimeProvider clock) { _clock = clock; }

    public string ProviderId => NotificationsConstants.Providers.Unifonic;
    public string Channel => NotificationsConstants.Channels.Sms;
    public bool SupportsMarket(string marketCode) => marketCode == NotificationsConstants.Markets.Sa;

    public Task<SendResult> SendAsync(NotificationDispatch dispatch, CancellationToken cancellationToken)
    {
        // Sandbox impl — real wiring uses a Refit client posting to
        // /api/v1/Messages/Send with a Bearer token from KV.
        var msgId = $"unifonic-sandbox-{dispatch.IdempotencyKey[..16]}";
        return Task.FromResult(SendResult.Ok(msgId));
    }

    public bool ValidateWebhookSignature(HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets)
    {
        vaultSecrets.TryGetValue("notifications-sms/sa/unifonic/webhook-signing-key", out var secret);
        return NotificationWebhookSignature.ValidateHexHmacSha256FromHeader(
            request, rawBody, "X-Unifonic-Signature", secret);
    }

    public WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var msgId = root.TryGetProperty("MessageID", out var mid) ? mid.GetString() ?? string.Empty : string.Empty;
        var status = root.TryGetProperty("Status", out var st) ? st.GetString() ?? "unknown" : "unknown";
        var canonical = status.ToLowerInvariant() switch
        {
            "delivered" or "sent" => CanonicalWebhookEventKinds.Delivered,
            "queued" or "accepted" => CanonicalWebhookEventKinds.Accepted,
            "failed" or "rejected" => CanonicalWebhookEventKinds.Failed,
            "undelivered" => CanonicalWebhookEventKinds.Bounced,
            _ => CanonicalWebhookEventKinds.Unknown,
        };
        string? errorCode = root.TryGetProperty("ErrorCode", out var ec) ? ec.GetString() : null;
        return new WebhookEvent(ProviderId, msgId, status, canonical, _clock.GetUtcNow(), errorCode);
    }
}
