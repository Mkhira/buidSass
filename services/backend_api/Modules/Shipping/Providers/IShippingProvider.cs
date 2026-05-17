using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Shipping.Providers;

/// <summary>
/// Provider abstraction per <c>contracts/shipping-contract.md §6</c>.
/// Every provider implementation MUST be config-replaceable (Principle 14);
/// business logic MUST NOT branch on <c>ProviderId</c> outside this folder.
/// </summary>
public interface IShippingProvider
{
    /// <summary>Canonical provider identifier (matches <c>ShippingConstants.Providers</c>).</summary>
    string ProviderId { get; }

    /// <summary>Returns <c>true</c> if the provider supports the given market.</summary>
    bool SupportsMarket(string marketCode);

    /// <summary>Calls the provider's create-shipment API. Idempotent on <c>dispatch.ShipmentId</c>.</summary>
    Task<CreateShipmentResult> CreateShipmentAsync(CreateShipmentDispatch dispatch, CancellationToken cancellationToken);

    /// <summary>Cancels / voids a previously-purchased label via the provider API.</summary>
    Task<VoidLabelResult> VoidLabelAsync(string trackingId, CancellationToken cancellationToken);

    /// <summary>Optional poll-mode tracking lookup. Webhooks are the primary path; polling is reconciliation.</summary>
    Task<TrackingResult> GetTrackingAsync(string trackingId, CancellationToken cancellationToken);

    /// <summary>Validates the incoming webhook HMAC signature against the KV-stored secret. Fail-closed.</summary>
    bool ValidateWebhookSignature(HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets);

    /// <summary>Parses the provider-specific webhook payload into the canonical event shape.</summary>
    WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody);
}

/// <summary>
/// Payload sent to <see cref="IShippingProvider.CreateShipmentAsync"/>. PII is minimized
/// per BR-15: recipient name redacted to first + last initial, phone masked to last-4.
/// </summary>
public sealed record CreateShipmentDispatch(
    Guid ShipmentId,
    string MarketCode,
    string MethodKey,
    string RecipientNameRedacted,
    string RecipientPhoneMaskedLast4,
    AddressMinimized ShipTo,
    decimal WeightKg,
    string CurrencyCode,
    decimal DeclaredValueAmount);

/// <summary>Address shape allowed at provider egress (BR-15 — minimum sufficient set).</summary>
public sealed record AddressMinimized(
    string City,
    string? PostalCode,
    string Line1,
    string? Line2,
    string CountryCode);

public sealed record CreateShipmentResult(
    bool Success,
    string? ProviderTrackingId,
    string? LabelPdfBlobUrl,
    DateTimeOffset? EtaMin,
    DateTimeOffset? EtaMax,
    string? FailureReason,
    string? FailureCode);

public sealed record VoidLabelResult(bool Success, string? FailureReason);

public sealed record TrackingResult(string ProviderEventKind, DateTimeOffset OccurredAt, string? RawPayloadRedactedJson);

/// <summary>
/// Canonical webhook event shape used by all providers. The handler maps
/// <see cref="CanonicalEventKind"/> onto the internal Shipment state machine.
/// </summary>
public sealed record WebhookEvent(
    string ProviderTrackingId,
    string ProviderEventKind,
    string CanonicalEventKind,
    DateTimeOffset OccurredAt,
    string RawPayloadRedactedJson);

/// <summary>Canonical event kinds shared across all providers.</summary>
public static class CanonicalEventKinds
{
    public const string LabelPurchased = "label_purchased";
    public const string HandedToCarrier = "handed_to_carrier";
    public const string InTransit = "in_transit";
    public const string OutForDelivery = "out_for_delivery";
    public const string Delivered = "delivered";
    public const string DeliveryAttempted = "delivery_attempted";
    public const string ReturnedToSender = "returned_to_sender";
}
