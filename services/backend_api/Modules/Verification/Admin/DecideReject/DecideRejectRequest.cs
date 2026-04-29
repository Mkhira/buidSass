using BackendApi.Modules.Verification.Admin.DecideApprove;

namespace BackendApi.Modules.Verification.Admin.DecideReject;

public sealed record DecideRejectRequest(ReviewerReason Reason);

public sealed record DecideRejectResponse(
    Guid Id,
    string State,
    DateTimeOffset DecidedAt,
    Guid DecidedBy,
    /// <summary>
    /// Earliest UTC instant the customer may submit a fresh verification.
    /// Computed as <c>DecidedAt + market.cooldown_days</c> (data-model §3.2 /
    /// FR-009). Surfaced so the customer-facing UI can render
    /// "you may resubmit on {date}".
    /// </summary>
    DateTimeOffset CooldownUntil);
