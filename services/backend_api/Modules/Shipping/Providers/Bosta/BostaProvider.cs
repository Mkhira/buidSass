using BackendApi.Modules.Shipping.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Shipping.Providers.Bosta;

/// <summary>Bosta — EG primary at v1.</summary>
public sealed class BostaProvider(HttpClient http, TimeProvider clock) : IShippingProvider
{
    private readonly HttpClient _http = http; // Reserved for Phase 1.5 real client wiring.

    public string ProviderId => ShippingConstants.Providers.Bosta;

    public bool SupportsMarket(string marketCode) => marketCode == ShippingConstants.Markets.EG;

    public Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentDispatch dispatch,
        CancellationToken cancellationToken)
    {
        var trackingId = $"BOSTA-{dispatch.ShipmentId:N}".ToUpperInvariant();
        var nowUtc = clock.GetUtcNow();
        return Task.FromResult(new CreateShipmentResult(
            true, trackingId,
            $"https://placeholder.invalid/labels/bosta/{trackingId}.pdf",
            nowUtc.AddHours(24), nowUtc.AddHours(72), null, null));
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
        vaultSecrets.TryGetValue("shipping/eg/bosta/api-key", out var secret);
        return ProviderWebhookSignature.ValidateHexHmacSha256FromHeader(
            request, rawBody, "X-Bosta-Signature", secret);
    }

    public WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
        => BostaWebhookMapper.Parse(rawBody, clock.GetUtcNow());
}
