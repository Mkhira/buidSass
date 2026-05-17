using System.Text.Json;
using BackendApi.Modules.Payments.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Payments.Providers.Tap;

/// <summary>Tap Payments — KSA cards backup (ADR-007 v1).</summary>
public sealed class TapProvider : PaymentProviderBase
{
    public TapProvider(TimeProvider clock) : base(clock) { }

    public override string ProviderId => PaymentsConstants.Providers.Tap;

    public override bool SupportsMarket(string marketCode) =>
        marketCode == PaymentsConstants.Markets.SA;

    public override bool SupportsLedgerApi => true;
    public override bool SupportsWebhookReplay => true;

    public override bool SupportsMethod(string method) => method switch
    {
        PaymentsConstants.Methods.Card => true,
        PaymentsConstants.Methods.Mada => true,
        _ => false,
    };

    public override bool ValidateWebhookSignature(
        HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets)
    {
        vaultSecrets.TryGetValue("payments/sa/tap/webhook-signing-key", out var secret);
        return ProviderWebhookSignature.ValidateHexHmacSha256FromHeader(
            request, rawBody, "X-Tap-Signature", secret);
    }

    public override WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var msgId = root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "unknown" : "unknown";
        var canonical = status switch
        {
            "CAPTURED" => CanonicalWebhookEventKinds.Captured,
            "AUTHORIZED" => CanonicalWebhookEventKinds.Authorized,
            "DECLINED" or "FAILED" => CanonicalWebhookEventKinds.Failed,
            "REFUNDED" => CanonicalWebhookEventKinds.Refunded,
            _ => CanonicalWebhookEventKinds.Unknown,
        };
        decimal? amount = null;
        if (root.TryGetProperty("amount", out var a) && a.TryGetDecimal(out var dec)) amount = dec;
        return new WebhookEvent(msgId, status, canonical, amount, Clock.GetUtcNow(), "{}");
    }
}
