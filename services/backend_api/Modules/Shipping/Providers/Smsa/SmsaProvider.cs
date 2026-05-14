using BackendApi.Modules.Shipping.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Shipping.Providers.Smsa;

/// <summary>
/// SMSA Express provider — KSA primary at v1 (per spec.md §clarifications).
/// API integration uses <see cref="HttpClient"/> with the API key sourced from
/// KV slot <c>shipping/sa/smsa/api-key</c>. Webhook signature header is
/// <c>X-Smsa-Signature</c> (HMAC-SHA256 over the raw body).
///
/// The CreateShipment / VoidLabel / GetTracking calls are placeholder
/// integrations — the real Refit-style client lands when provider credentials
/// are PIM-provisioned in staging. Webhook parsing IS production-ready: the
/// canonical event mapping is fixed by SMSA's published API and exercised in
/// tests.
/// </summary>
public sealed class SmsaProvider(HttpClient http, TimeProvider clock) : IShippingProvider
{
    // Reserved for the production Refit-style client wiring (Phase 1.5).
    // Held here so the typed-client registration in DI binds the right surface.
    private readonly HttpClient _http = http;

    public string ProviderId => ShippingConstants.Providers.Smsa;

    public bool SupportsMarket(string marketCode) =>
        marketCode == ShippingConstants.Markets.SA;

    public Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentDispatch dispatch,
        CancellationToken cancellationToken)
    {
        // Stub — returns a deterministic tracking id derived from the shipment id
        // so end-to-end smoke tests can be deterministic without network IO.
        // Real implementation hits SMSA's CreateAWB endpoint.
        var trackingId = $"SMSA-{dispatch.ShipmentId:N}".ToUpperInvariant();
        var nowUtc = clock.GetUtcNow();
        return Task.FromResult(new CreateShipmentResult(
            Success: true,
            ProviderTrackingId: trackingId,
            LabelPdfBlobUrl: $"https://placeholder.invalid/labels/smsa/{trackingId}.pdf",
            EtaMin: nowUtc.AddHours(36),
            EtaMax: nowUtc.AddHours(72),
            FailureReason: null,
            FailureCode: null));
    }

    public Task<VoidLabelResult> VoidLabelAsync(string trackingId, CancellationToken cancellationToken)
        => Task.FromResult(new VoidLabelResult(Success: true, FailureReason: null));

    public Task<TrackingResult> GetTrackingAsync(string trackingId, CancellationToken cancellationToken)
        => Task.FromResult(new TrackingResult(CanonicalEventKinds.InTransit, clock.GetUtcNow(), "{}"));

    public bool ValidateWebhookSignature(
        HttpRequest request,
        byte[] rawBody,
        IReadOnlyDictionary<string, string> vaultSecrets)
    {
        vaultSecrets.TryGetValue("shipping/sa/smsa/api-key", out var secret);
        return ProviderWebhookSignature.ValidateHexHmacSha256FromHeader(
            request, rawBody, "X-Smsa-Signature", secret);
    }

    public WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
        => SmsaWebhookMapper.Parse(rawBody, clock.GetUtcNow());
}
