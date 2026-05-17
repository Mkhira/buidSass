using System.Text.Json;
using BackendApi.Modules.Payments.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Payments.Providers.Tabby;

/// <summary>Tabby — KSA BNPL primary (ADR-007 v1). Redirect flow; webhook-driven capture.</summary>
public sealed class TabbyProvider : PaymentProviderBase
{
    public TabbyProvider(TimeProvider clock) : base(clock) { }

    public override string ProviderId => PaymentsConstants.Providers.Tabby;

    public override bool SupportsMarket(string marketCode) =>
        marketCode == PaymentsConstants.Markets.SA;

    public override bool SupportsMethod(string method) =>
        method == PaymentsConstants.Methods.BnplTabby;

    protected override bool CaptureIsSynchronous => false;

    public override bool ValidateWebhookSignature(
        HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets)
    {
        vaultSecrets.TryGetValue("payments/sa/tabby/webhook-signing-key", out var secret);
        return ProviderWebhookSignature.ValidateBase64HmacSha256FromHeader(
            request, rawBody, "x-tabby-signature", secret);
    }

    public override WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var msgId = root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "REJECTED" : "REJECTED";
        var canonical = status switch
        {
            "AUTHORIZED" or "CLOSED" => CanonicalWebhookEventKinds.Captured,
            "REJECTED" or "EXPIRED" => CanonicalWebhookEventKinds.Failed,
            _ => CanonicalWebhookEventKinds.Failed,
        };
        decimal? amount = null;
        if (root.TryGetProperty("amount", out var a) && a.TryGetDecimal(out var dec)) amount = dec;
        return new WebhookEvent(msgId, status, canonical, amount, Clock.GetUtcNow(), "{}");
    }
}
