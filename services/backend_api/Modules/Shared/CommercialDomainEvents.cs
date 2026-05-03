using MediatR;

namespace BackendApi.Modules.Shared;

/// <summary>
/// Spec 007-b commercial-authoring domain events (data-model §6).
/// All implement <see cref="INotification"/> for the in-process bus.
/// Consumed by spec 025 notifications and (for deactivation events) spec 010 checkout.
/// </summary>
public sealed record CouponActivated(
    Guid CouponId,
    string Code,
    string[] MarketCodes,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    DateTimeOffset ActivatedAtUtc,
    Guid ActorId) : INotification;

public sealed record CouponExpired(
    Guid CouponId,
    DateTimeOffset ExpiredAtUtc) : INotification;

public sealed record CouponDeactivated(
    Guid CouponId,
    DateTimeOffset DeactivatedAtUtc,
    Guid DeactivatedByActorId,
    string ReasonNote,
    int InFlightGraceSeconds) : INotification;

public sealed record CouponReactivated(
    Guid CouponId,
    DateTimeOffset ReactivatedAtUtc,
    Guid ActorId) : INotification;

public sealed record PromotionActivated(
    Guid PromotionId,
    string Name,
    string[] MarketCodes,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    DateTimeOffset ActivatedAtUtc,
    Guid ActorId) : INotification;

public sealed record PromotionExpired(
    Guid PromotionId,
    DateTimeOffset ExpiredAtUtc) : INotification;

public sealed record PromotionDeactivated(
    Guid PromotionId,
    DateTimeOffset DeactivatedAtUtc,
    Guid DeactivatedByActorId,
    string ReasonNote,
    int InFlightGraceSeconds) : INotification;

public sealed record PromotionReactivated(
    Guid PromotionId,
    DateTimeOffset ReactivatedAtUtc,
    Guid ActorId) : INotification;

public sealed record CampaignLinkBroken(
    Guid CampaignId,
    Guid? BrokenLinkTargetId,
    string BrokenLinkKind,
    DateTimeOffset BrokenAtUtc) : INotification;

public sealed record CommercialThresholdChanged(
    string MarketCode,
    string BeforeJson,
    string AfterJson,
    Guid ActorId,
    DateTimeOffset ChangedAtUtc) : INotification;
