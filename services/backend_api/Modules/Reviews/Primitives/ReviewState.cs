namespace BackendApi.Modules.Reviews.Primitives;

/// <summary>
/// Lifecycle states of a customer review per spec 022 data-model §3.
/// </summary>
public enum ReviewState
{
    PendingModeration = 0,
    Visible = 1,
    Flagged = 2,
    Hidden = 3,
    Deleted = 4,
}
