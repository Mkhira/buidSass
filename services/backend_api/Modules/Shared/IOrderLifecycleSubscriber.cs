namespace BackendApi.Modules.Shared;

/// <summary>
/// Cross-module hook for the Order aggregate's lifecycle events (per spec 011 §events).
/// Lives in <c>Modules/Shared/</c> so the Shipping / Notifications / Analytics modules
/// can subscribe without forming a module-graph cycle on Orders. The Order module's
/// state-transition handlers fan-out to every registered implementation.
///
/// Spec 026 implements this in <c>Modules/Shipping/Subscribers/</c> to drive shipment
/// creation on <c>order.confirmed</c> and label-void cascades on <c>order.cancelled</c>.
/// </summary>
public interface IOrderLifecycleSubscriber
{
    Task OnOrderConfirmedAsync(OrderConfirmedEvent @event, CancellationToken cancellationToken);
    Task OnOrderCancelledAsync(OrderCancelledEvent @event, CancellationToken cancellationToken);
    Task OnOrderRefundInitiatedAsync(OrderRefundInitiatedEvent @event, CancellationToken cancellationToken);
}

/// <summary>
/// Payload published when an order is confirmed (payment authorized and ready to ship).
/// Carries the minimum recipient info needed for downstream subscribers; PII handling
/// is the consumer's responsibility (Shipping's BR-15 redaction applies on egress).
/// </summary>
public sealed record OrderConfirmedEvent(
    Guid OrderId,
    string MarketCode,
    Guid? CustomerId,
    Guid? CompanyId,
    Guid? ShippingMethodVersionId,
    decimal WeightKg,
    decimal CurrencyAmount,
    string CurrencyCode,
    OrderShipToAddress ShipTo,
    DateTimeOffset OccurredAt);

public sealed record OrderCancelledEvent(
    Guid OrderId,
    string MarketCode,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed record OrderRefundInitiatedEvent(
    Guid OrderId,
    string MarketCode,
    bool RequiresReturnShipment,
    DateTimeOffset OccurredAt);

/// <summary>Order's ship-to address pre-redaction. The Shipping module redacts on egress per BR-15.</summary>
public sealed record OrderShipToAddress(
    string RecipientName,
    string RecipientPhone,
    string City,
    string? PostalCode,
    string Line1,
    string? Line2,
    string CountryCode);
