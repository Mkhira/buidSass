using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Notifications.Providers.Infobip;

/// <summary>
/// Infobip — SMS backup provider for both markets. Webhooks deliver DLRs;
/// signature is base64-encoded HMAC-SHA256 in <c>X-Signature</c>. The same
/// adapter handles both KSA and EG routing; market selection is upstream
/// (provider-routing). ADR-009 v1 backup.
/// </summary>
public sealed class InfobipSmsProvider : INotificationProvider
{
    private readonly TimeProvider _clock;

    public InfobipSmsProvider(TimeProvider clock) { _clock = clock; }

    public string ProviderId => NotificationsConstants.Providers.Infobip;
    public string Channel => NotificationsConstants.Channels.Sms;
    public bool SupportsMarket(string marketCode) => NotificationsConstants.Markets.All.Contains(marketCode);

    public Task<SendResult> SendAsync(NotificationDispatch dispatch, CancellationToken cancellationToken)
    {
        var msgId = $"infobip-sandbox-{dispatch.IdempotencyKey[..16]}";
        return Task.FromResult(SendResult.Ok(msgId));
    }

    public bool ValidateWebhookSignature(HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets)
    {
        // Try the active per-market vault key first (callers route via market).
        var saSecretOk = vaultSecrets.TryGetValue(
            "notifications-sms/sa/infobip/webhook-signing-key", out var saSecret);
        var egSecretOk = vaultSecrets.TryGetValue(
            "notifications-sms/eg/infobip/webhook-signing-key", out var egSecret);

        if (saSecretOk && NotificationWebhookSignature.ValidateBase64HmacSha256FromHeader(
                request, rawBody, "X-Signature", saSecret))
            return true;
        if (egSecretOk && NotificationWebhookSignature.ValidateBase64HmacSha256FromHeader(
                request, rawBody, "X-Signature", egSecret))
            return true;
        return false;
    }

    public WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
    {
        // Infobip DLR payloads wrap an array under "results".
        using var doc = JsonDocument.Parse(rawBody);
        if (!doc.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
        {
            return new WebhookEvent(ProviderId, string.Empty, "empty", CanonicalWebhookEventKinds.Unknown, _clock.GetUtcNow(), null);
        }

        var first = results[0];
        var msgId = first.TryGetProperty("messageId", out var mid) ? mid.GetString() ?? string.Empty : string.Empty;
        var group = first.TryGetProperty("status", out var st) && st.TryGetProperty("groupName", out var gn)
            ? gn.GetString() ?? "unknown" : "unknown";
        var canonical = group.ToUpperInvariant() switch
        {
            "DELIVERED" => CanonicalWebhookEventKinds.Delivered,
            "PENDING" => CanonicalWebhookEventKinds.Accepted,
            "UNDELIVERABLE" => CanonicalWebhookEventKinds.Bounced,
            "REJECTED" => CanonicalWebhookEventKinds.Failed,
            "EXPIRED" => CanonicalWebhookEventKinds.SoftBounced,
            _ => CanonicalWebhookEventKinds.Unknown,
        };
        string? errorCode = first.TryGetProperty("error", out var err) && err.TryGetProperty("name", out var en)
            ? en.GetString() : null;
        return new WebhookEvent(ProviderId, msgId, group, canonical, _clock.GetUtcNow(), errorCode);
    }
}
