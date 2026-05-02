namespace BackendApi.Modules.Cms.Primitives;

/// <summary>
/// Unified 4-state content lifecycle shared by all 5 entity kinds, plus the
/// <see cref="Superseded"/> terminal that legal-page versions extend with.
/// Per spec 024 data-model §3.
/// </summary>
public enum ContentLifecycleState
{
    Draft = 0,
    Scheduled = 1,
    Live = 2,
    Archived = 3,
    Superseded = 4,
}

public static class ContentLifecycleStateWire
{
    public static string ToWire(this ContentLifecycleState state) => state switch
    {
        ContentLifecycleState.Draft => "draft",
        ContentLifecycleState.Scheduled => "scheduled",
        ContentLifecycleState.Live => "live",
        ContentLifecycleState.Archived => "archived",
        ContentLifecycleState.Superseded => "superseded",
        _ => throw new InvalidOperationException($"Unknown ContentLifecycleState: {state}"),
    };

    public static ContentLifecycleState FromWire(string s) => s switch
    {
        "draft" => ContentLifecycleState.Draft,
        "scheduled" => ContentLifecycleState.Scheduled,
        "live" => ContentLifecycleState.Live,
        "archived" => ContentLifecycleState.Archived,
        "superseded" => ContentLifecycleState.Superseded,
        _ => throw new InvalidOperationException($"Unknown ContentLifecycleState wire value: '{s}'."),
    };
}
