namespace BackendApi.Modules.Reviews.Admin.DecideModeration;

/// <summary>Request body for POST /api/admin/reviews/{id}/decide per contract §3.3.</summary>
public sealed record DecideModerationRequest(
    string ToState,
    string? ReasonNote,
    string? AdminNote);
