using MediatR;

namespace BackendApi.Modules.Notifications.Subscribers;

/// <summary>
/// Upstream domain events the Notifications module subscribes to. These are
/// the contracts other modules MUST publish through MediatR for transactional
/// notifications to fire. Defined here rather than per-publishing-module so
/// the Notifications module is self-contained per Principle 19 / contract §4.
/// Upstream modules either implement these directly or adapt their existing
/// event types into these shapes (publisher-side projection).
///
/// Locale + market are intentionally surfaced on every event so the
/// Notifications module can pick the correct template version without a
/// secondary lookup against the originating module.
/// </summary>

public sealed record AuthOtpRequested(
    Guid CustomerId,
    string Recipient,
    string Channel,
    string Locale,
    string MarketCode,
    string OtpCode,
    int TtlSeconds,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record OrderPlaced(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    decimal TotalAmount,
    string Currency,
    string Locale,
    string MarketCode,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record OrderConfirmed(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    string Locale,
    string MarketCode,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record OrderShipped(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    string Carrier,
    string TrackingNumber,
    string Locale,
    string MarketCode,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record OrderDelivered(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    string Locale,
    string MarketCode,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record OrderCancelled(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    string CancellationReason,
    string Locale,
    string MarketCode,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record OrderRefundInitiated(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    decimal RefundAmount,
    string Currency,
    string Locale,
    string MarketCode,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record OrderRefundCompleted(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    decimal RefundAmount,
    string Currency,
    string Locale,
    string MarketCode,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record VerificationApproved(
    Guid CustomerId,
    Guid VerificationId,
    string Locale,
    string MarketCode,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record VerificationRejected(
    Guid CustomerId,
    Guid VerificationId,
    string ReasonCode,
    string Locale,
    string MarketCode,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record PricingPriceDropped(
    Guid CustomerId,
    Guid ProductId,
    string ProductName,
    decimal OldPrice,
    decimal NewPrice,
    string Currency,
    string Locale,
    string MarketCode,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record InventoryRestocked(
    Guid CustomerId,
    Guid ProductId,
    string ProductName,
    string Locale,
    string MarketCode,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record CartAbandoned24h(
    Guid CustomerId,
    Guid CartId,
    int ItemCount,
    decimal CartTotal,
    string Currency,
    string Locale,
    string MarketCode,
    DateTimeOffset OccurredAtUtc) : INotification;

public sealed record ShippingStatusChanged(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    string StatusCode,
    string? CarrierMessage,
    string Locale,
    string MarketCode,
    DateTimeOffset OccurredAtUtc) : INotification;
