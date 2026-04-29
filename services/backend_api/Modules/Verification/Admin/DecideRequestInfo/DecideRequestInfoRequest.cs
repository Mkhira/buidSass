using BackendApi.Modules.Verification.Admin.DecideApprove;

namespace BackendApi.Modules.Verification.Admin.DecideRequestInfo;

public sealed record DecideRequestInfoRequest(ReviewerReason Reason);

public sealed record DecideRequestInfoResponse(
    Guid Id,
    string State,
    DateTimeOffset RequestedAt,
    Guid RequestedBy);
