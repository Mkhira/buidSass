using System.Text.Json;
using BackendApi.Modules.Payments.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Payments.Providers.Valu;

/// <summary>
/// Valu — EG BNPL primary (ADR-007 v1). Redirect flow. Per research §4 and
/// §8, Valu does NOT expose a webhook-replay API at v1; reconciliation
/// fallback is the documented mitigation. Settlement ledger is SFTP-CSV.
/// </summary>
public sealed class ValuProvider : PaymentProviderBase
{
    public ValuProvider(TimeProvider clock) : base(clock) { }

    public override string ProviderId => PaymentsConstants.Providers.Valu;

    public override bool SupportsMarket(string marketCode) =>
        marketCode == PaymentsConstants.Markets.EG;

    public override bool SupportsMethod(string method) =>
        method == PaymentsConstants.Methods.BnplValu;

    protected override bool CaptureIsSynchronous => false;
    public override bool SupportsLedgerApi => false;
    public override bool SupportsWebhookReplay => false;

    public override bool ValidateWebhookSignature(
        HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets)
    {
        vaultSecrets.TryGetValue("payments/eg/valu/webhook-signing-key", out var secret);
        return ProviderWebhookSignature.ValidateHexHmacSha256FromHeader(
            request, rawBody, "X-Valu-Signature", secret);
    }

    public override WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var msgId = root.TryGetProperty("reference", out var id) ? id.GetString() ?? string.Empty : string.Empty;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "REJECTED" : "REJECTED";
        var canonical = status switch
        {
            "APPROVED" or "DISBURSED" => CanonicalWebhookEventKinds.Captured,
            "REJECTED" or "CANCELLED" => CanonicalWebhookEventKinds.Failed,
            "REFUNDED" => CanonicalWebhookEventKinds.Refunded,
            _ => CanonicalWebhookEventKinds.Failed,
        };
        decimal? amount = null;
        if (root.TryGetProperty("amount", out var a) && a.TryGetDecimal(out var dec)) amount = dec;
        return new WebhookEvent(msgId, status, canonical, amount, Clock.GetUtcNow(), "{}");
    }
}
