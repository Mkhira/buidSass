using BackendApi.Modules.Verification.Admin.DecideApprove;

namespace BackendApi.Modules.Verification.Admin.DecideRevoke;

public sealed record DecideRevokeRequest(ReviewerReason Reason);

public sealed record DecideRevokeResponse(
    Guid Id,
    string State,
    DateTimeOffset RevokedAt,
    Guid RevokedBy);
