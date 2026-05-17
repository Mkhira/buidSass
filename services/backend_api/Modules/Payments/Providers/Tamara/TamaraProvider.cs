using System.Text.Json;
using BackendApi.Modules.Payments.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Payments.Providers.Tamara;

/// <summary>Tamara — KSA BNPL primary (ADR-007 v1). Redirect flow; webhook-driven capture.</summary>
public sealed class TamaraProvider : PaymentProviderBase
{
    public TamaraProvider(TimeProvider clock) : base(clock) { }

    public override string ProviderId => PaymentsConstants.Providers.Tamara;

    public override bool SupportsMarket(string marketCode) =>
        marketCode == PaymentsConstants.Markets.SA;

    public override bool SupportsMethod(string method) =>
        method == PaymentsConstants.Methods.BnplTamara;

    protected override bool CaptureIsSynchronous => false;

    public override bool ValidateWebhookSignature(
        HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets)
    {
        vaultSecrets.TryGetValue("payments/sa/tamara/webhook-signing-key", out var secret);
        return ProviderWebhookSignature.ValidateBase64HmacSha256FromHeader(
            request, rawBody, "tamara-signature", secret);
    }

    public override WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var msgId = root.TryGetProperty("order_id", out var id) ? id.GetString() ?? string.Empty : string.Empty;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "canceled" : "canceled";
        var canonical = status switch
        {
            "approved" or "fully_captured" => CanonicalWebhookEventKinds.Captured,
            "declined" or "canceled" or "expired" => CanonicalWebhookEventKinds.Failed,
            "refunded" => CanonicalWebhookEventKinds.Refunded,
            _ => CanonicalWebhookEventKinds.Failed,
        };
        decimal? amount = null;
        if (root.TryGetProperty("total_amount", out var a) && a.TryGetDecimal(out var dec)) amount = dec;
        return new WebhookEvent(msgId, status, canonical, amount, Clock.GetUtcNow(), "{}");
    }
}
