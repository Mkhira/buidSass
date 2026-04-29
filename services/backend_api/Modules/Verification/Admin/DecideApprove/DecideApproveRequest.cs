namespace BackendApi.Modules.Verification.Admin.DecideApprove;

/// <summary>
/// Reviewer-supplied bilingual reason per spec 020 contracts §3.3 / FR-033.
/// At least one of <see cref="En"/> / <see cref="Ar"/> MUST be present and
/// non-blank; both preserved in the audit log; customer rendering uses the
/// customer's preferred locale with a "(reviewer left this in {OtherLocale})"
/// notice when one side is missing.
/// </summary>
public sealed record ReviewerReason(string? En, string? Ar);

public sealed record DecideApproveRequest(ReviewerReason Reason);

public sealed record DecideApproveResponse(
    Guid Id,
    string State,
    DateTimeOffset DecidedAt,
    Guid DecidedBy,
    DateTimeOffset ExpiresAt,
    Guid? SupersededId);
