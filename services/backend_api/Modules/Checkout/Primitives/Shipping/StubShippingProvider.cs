namespace BackendApi.Modules.Checkout.Primitives.Shipping;

/// <summary>
/// Dev/test shipping provider. Returns two deterministic methods per market so the quote
/// endpoint has something to surface. Real carriers replace this per ADR-008.
/// </summary>
public sealed class StubShippingProvider : IShippingProvider
{
    public string ProviderId => "stub";

    public bool Supports(string marketCode) =>
        string.Equals(marketCode, "ksa", StringComparison.OrdinalIgnoreCase)
        || string.Equals(marketCode, "eg", StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<ShippingQuoteOffer>> QuoteAsync(QuoteRequest request, CancellationToken ct)
    {
        // CR review on PR #30: fail closed when the caller forgets to gate on Supports() — the
        // stub used to return EGP quotes for any market, hiding misuse during integration work.
        if (!Supports(request.MarketCode))
        {
            return Task.FromResult<IReadOnlyList<ShippingQuoteOffer>>(Array.Empty<ShippingQuoteOffer>());
        }
        var currency = string.Equals(request.MarketCode, "ksa", StringComparison.OrdinalIgnoreCase) ? "SAR" : "EGP";
        IReadOnlyList<ShippingQuoteOffer> offers = new[]
        {
            new ShippingQuoteOffer("standard", 3, 5, 2500, currency),
            new ShippingQuoteOffer("express", 1, 2, 7500, currency),
        };
        return Task.FromResult(offers);
    }

    public Task<CreateShipmentOutcome> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken ct)
    {
        // Deterministic tracking number derived from the order so tests can assert on it.
        var tracking = $"STUB-{request.OrderNumber}";
        return Task.FromResult(new CreateShipmentOutcome(
            IsSuccess: true,
            TrackingNumber: tracking,
            LabelUrl: $"https://stub.example/labels/{tracking}.pdf"));
    }

    public Task<TrackingSnapshot> TrackAsync(string trackingNumber, CancellationToken ct)
        => Task.FromResult(new TrackingSnapshot(
            trackingNumber,
            "in_transit",
            EstimatedDeliveryAt: DateTimeOffset.UtcNow.AddDays(2),
            Events: Array.Empty<TrackingEvent>()));
}
