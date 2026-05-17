using BackendApi.Modules.Shipping.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Shipping.Providers.Aramex;

/// <summary>Aramex KSA — KSA backup at v1.</summary>
public sealed class AramexKsaProvider(HttpClient http, TimeProvider clock) : IShippingProvider
{
    private readonly HttpClient _http = http; // Reserved for Phase 1.5 real client wiring.

    public string ProviderId => ShippingConstants.Providers.AramexKsa;

    public bool SupportsMarket(string marketCode) => marketCode == ShippingConstants.Markets.SA;

    public Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentDispatch dispatch,
        CancellationToken cancellationToken)
    {
        var trackingId = $"ARMX-KSA-{dispatch.ShipmentId:N}".ToUpperInvariant();
        var nowUtc = clock.GetUtcNow();
        return Task.FromResult(new CreateShipmentResult(
            true, trackingId,
            $"https://placeholder.invalid/labels/aramex-ksa/{trackingId}.pdf",
            nowUtc.AddHours(48), nowUtc.AddHours(96), null, null));
    }

    public Task<VoidLabelResult> VoidLabelAsync(string trackingId, CancellationToken cancellationToken)
        => Task.FromResult(new VoidLabelResult(true, null));

    public Task<TrackingResult> GetTrackingAsync(string trackingId, CancellationToken cancellationToken)
        => Task.FromResult(new TrackingResult(CanonicalEventKinds.InTransit, clock.GetUtcNow(), "{}"));

    public bool ValidateWebhookSignature(
        HttpRequest request,
        byte[] rawBody,
        IReadOnlyDictionary<string, string> vaultSecrets)
    {
        vaultSecrets.TryGetValue("shipping/sa/aramex-ksa/api-key", out var secret);
        return ProviderWebhookSignature.ValidateHexHmacSha256FromHeader(
            request, rawBody, "X-Aramex-Signature", secret);
    }

    public WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
        => AramexWebhookMapper.Parse(rawBody, clock.GetUtcNow());
}

/// <summary>Aramex EG — EG backup at v1.</summary>
public sealed class AramexEgProvider(HttpClient http, TimeProvider clock) : IShippingProvider
{
    private readonly HttpClient _http = http; // Reserved for Phase 1.5 real client wiring.

    public string ProviderId => ShippingConstants.Providers.AramexEg;

    public bool SupportsMarket(string marketCode) => marketCode == ShippingConstants.Markets.EG;

    public Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentDispatch dispatch,
        CancellationToken cancellationToken)
    {
        var trackingId = $"ARMX-EG-{dispatch.ShipmentId:N}".ToUpperInvariant();
        var nowUtc = clock.GetUtcNow();
        return Task.FromResult(new CreateShipmentResult(
            true, trackingId,
            $"https://placeholder.invalid/labels/aramex-eg/{trackingId}.pdf",
            nowUtc.AddHours(48), nowUtc.AddHours(120), null, null));
    }

    public Task<VoidLabelResult> VoidLabelAsync(string trackingId, CancellationToken cancellationToken)
        => Task.FromResult(new VoidLabelResult(true, null));

    public Task<TrackingResult> GetTrackingAsync(string trackingId, CancellationToken cancellationToken)
        => Task.FromResult(new TrackingResult(CanonicalEventKinds.InTransit, clock.GetUtcNow(), "{}"));

    public bool ValidateWebhookSignature(
        HttpRequest request,
        byte[] rawBody,
        IReadOnlyDictionary<string, string> vaultSecrets)
    {
        vaultSecrets.TryGetValue("shipping/eg/aramex-eg/api-key", out var secret);
        return ProviderWebhookSignature.ValidateHexHmacSha256FromHeader(
            request, rawBody, "X-Aramex-Signature", secret);
    }

    public WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
        => AramexWebhookMapper.Parse(rawBody, clock.GetUtcNow());
}
