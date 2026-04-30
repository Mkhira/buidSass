namespace BackendApi.Modules.Reviews.Admin.DecideModeration;

public sealed record DecideModerationResponse(
    Guid Id,
    string State,
    uint RowVersion,
    DateTimeOffset StateChangedAtUtc,
    Guid StateChangedByActorId);
