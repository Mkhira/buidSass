using MediatR;

namespace BackendApi.Modules.Shipping.Domain.Events;

/// <summary>
/// Shipping domain events published via MediatR. Spec 025 (notifications)
/// subscribes to <see cref="ShipmentStatusChanged"/> per contract §5.
/// Naming mirrors the Reviews / Support spec conventions.
/// </summary>

/// <summary>Emitted when a shipment row first persists a label_purchased state.</summary>
public sealed record ShipmentLabelPurchased(
    Guid ShipmentId,
    Guid OrderId,
    string MarketCode,
    string ProviderId,
    string ProviderTrackingId,
    DateTimeOffset OccurredAt) : INotification;

/// <summary>
/// Emitted on every shipment-state advance (Flow 3). Spec 025 consumes this
/// and dispatches the per-market localized notification.
/// </summary>
public sealed record ShipmentStatusChanged(
    Guid ShipmentId,
    Guid OrderId,
    string MarketCode,
    string ProviderId,
    string? ProviderTrackingId,
    string FromState,
    string ToState,
    DateTimeOffset OccurredAt) : INotification;

public sealed record ShipmentDeliveryDisputed(
    Guid ShipmentId,
    Guid OrderId,
    string MarketCode,
    DateTimeOffset OccurredAt) : INotification;

public sealed record ShipmentReDeliveryCreated(
    Guid NewShipmentId,
    Guid ParentShipmentId,
    Guid OrderId,
    string MarketCode,
    DateTimeOffset OccurredAt) : INotification;

public sealed record ShipmentSlaBreached(
    Guid ShipmentId,
    Guid OrderId,
    string MarketCode,
    int AgeHours,
    DateTimeOffset OccurredAt) : INotification;

public sealed record ProviderDegraded(
    string ProviderId,
    string MarketCode,
    int FailurePct,
    DateTimeOffset OccurredAt) : INotification;

public sealed record ProviderFailoverPerformed(
    string MarketCode,
    Guid MethodId,
    string FromProviderId,
    string ToProviderId,
    DateTimeOffset OccurredAt) : INotification;
