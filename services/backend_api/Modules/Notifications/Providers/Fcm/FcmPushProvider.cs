using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Notifications.Providers.Fcm;

/// <summary>
/// FCM — primary push provider, both markets. Send-side uses FirebaseAdmin
/// SDK with a service-account JSON loaded from KV per research §5. Webhook-
/// side: FCM uses OIDC token verification (Google-issued JWT in
/// <c>Authorization: Bearer</c>) — for sandbox we accept a vault-signed
/// HMAC-SHA256 header as a fallback so test fixtures can exercise the path
/// without a live Google identity broker. ADR-009 v1 stack.
/// </summary>
public sealed class FcmPushProvider : INotificationProvider
{
    private readonly TimeProvider _clock;

    public FcmPushProvider(TimeProvider clock) { _clock = clock; }

    public string ProviderId => NotificationsConstants.Providers.Fcm;
    public string Channel => NotificationsConstants.Channels.Push;
    public bool SupportsMarket(string marketCode) => NotificationsConstants.Markets.All.Contains(marketCode);

    public Task<SendResult> SendAsync(NotificationDispatch dispatch, CancellationToken cancellationToken)
    {
        // Sandbox impl. Real wiring will use FirebaseMessaging.GetMessaging(app).SendAsync(message).
        // FCM exposes an "unregistered" terminal error code on dead tokens —
        // the worker will mark the push token invalid in that case (skipped reason).
        var msgId = $"fcm-sandbox-{dispatch.IdempotencyKey[..16]}";
        return Task.FromResult(SendResult.Ok(msgId));
    }

    public bool ValidateWebhookSignature(HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets)
    {
        // Production: OIDC token verification via Google JWKS — performed by the
        // ASP.NET auth middleware ahead of this method (audience = our service URL).
        // If the middleware admitted the request, treat it as authentic.
        if (request.HttpContext.User?.Identity?.IsAuthenticated == true)
            return true;

        // Fallback for sandbox/test: HMAC-SHA256 against the vault secret.
        vaultSecrets.TryGetValue("notifications-push/multi/fcm/webhook-signing-key", out var secret);
        return NotificationWebhookSignature.ValidateHexHmacSha256FromHeader(
            request, rawBody, "X-FCM-HMAC", secret);
    }

    public WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var msgId = root.TryGetProperty("messageId", out var mid) ? mid.GetString() ?? string.Empty : string.Empty;
        var ev = root.TryGetProperty("event", out var evEl) ? evEl.GetString() ?? "unknown" : "unknown";
        var canonical = ev.ToLowerInvariant() switch
        {
            "delivered" or "delivery" => CanonicalWebhookEventKinds.Delivered,
            "unregistered" or "token_invalid" => CanonicalWebhookEventKinds.Unregistered,
            "failed" or "error" => CanonicalWebhookEventKinds.Failed,
            _ => CanonicalWebhookEventKinds.Unknown,
        };
        string? errorCode = root.TryGetProperty("error", out var err) ? err.GetString() : null;
        return new WebhookEvent(ProviderId, msgId, ev, canonical, _clock.GetUtcNow(), errorCode);
    }
}
