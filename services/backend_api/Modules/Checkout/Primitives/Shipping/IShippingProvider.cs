namespace BackendApi.Modules.Checkout.Primitives.Shipping;

/// <summary>
/// Shipping provider abstraction (FR-006, Principle 14). Real carriers (SMSA, Aramex, etc.)
/// implement this interface; spec 010 ships only a StubShippingProvider — ADR-008 picks
/// the launch providers per market later in the phase.
/// </summary>
public interface IShippingProvider
{
    string ProviderId { get; }

    /// <summary>True when this provider covers the given market.</summary>
    bool Supports(string marketCode);

    Task<IReadOnlyList<ShippingQuoteOffer>> QuoteAsync(QuoteRequest request, CancellationToken ct);
    Task<CreateShipmentOutcome> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken ct);
    Task<TrackingSnapshot> TrackAsync(string trackingNumber, CancellationToken ct);
}

public sealed record QuoteRequest(
    string MarketCode,
    ShippingAddress DestinationAddress,
    decimal PackageWeightKg,
    long DeclaredValueMinor,
    string Currency);

public sealed record ShippingQuoteOffer(
    string MethodCode,
    int EtaMinDays,
    int EtaMaxDays,
    long FeeMinor,
    string Currency,
    string PayloadJson = "{}");

public sealed record CreateShipmentRequest(
    string MethodCode,
    Guid OrderId,
    string OrderNumber,
    ShippingAddress DestinationAddress);

public sealed record CreateShipmentOutcome(
    bool IsSuccess,
    string? TrackingNumber,
    string? LabelUrl,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record TrackingSnapshot(
    string TrackingNumber,
    string Status,
    DateTimeOffset? EstimatedDeliveryAt,
    IReadOnlyList<TrackingEvent> Events);

public sealed record TrackingEvent(DateTimeOffset OccurredAt, string Status, string? Location, string? Description);

public sealed record ShippingAddress(
    string FullName,
    string PhoneE164,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string? PostalCode,
    string CountryCode);
