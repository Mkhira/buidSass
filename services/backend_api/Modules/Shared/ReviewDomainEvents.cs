using MediatR;

namespace BackendApi.Modules.Shared;

/// <summary>
/// 8 lifecycle domain events emitted by spec 022 and consumed by spec 025
/// (notifications) per data-model §6. Notifications MUST NOT block the
/// originating lifecycle transaction (FR-038); publishers fire AFTER commit.
/// </summary>
public sealed record ReviewSubmitted(
    Guid ReviewId,
    Guid CustomerId,
    Guid ProductId,
    string MarketCode,
    string Locale,
    int Rating,
    bool HasMedia,
    bool WasHeld) : INotification;

public sealed record ReviewPublished(
    Guid ReviewId,
    Guid ProductId,
    string MarketCode,
    int Rating,
    DateTimeOffset TransitionedAtUtc) : INotification;

public sealed record ReviewHeldForModeration(
    Guid ReviewId,
    Guid CustomerId,
    string HoldReason,
    int? TermCount) : INotification;

public sealed record ReviewFlagged(
    Guid ReviewId,
    int QualifiedReportCount,
    int Threshold) : INotification;

public sealed record ReviewHidden(
    Guid ReviewId,
    Guid ActorId,
    string ReasonNote) : INotification;

public sealed record ReviewDeleted(
    Guid ReviewId,
    Guid ActorId) : INotification;

public sealed record ReviewReinstated(
    Guid ReviewId,
    Guid ActorId,
    string PriorState) : INotification;

public sealed record ReviewAutoHidden(
    Guid ReviewId,
    string Trigger,
    Guid? SourceEventId) : INotification;
