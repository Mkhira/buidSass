namespace BackendApi.Modules.Returns.Primitives;

/// <summary>
/// SM-3. Inspection state machine.
/// </summary>
public static class InspectionStateMachine
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Complete = "complete";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Pending, InProgress, Complete,
    };

    public static bool IsValidTransition(string from, string to)
    {
        var f = from?.ToLowerInvariant() ?? string.Empty;
        var t = to?.ToLowerInvariant() ?? string.Empty;
        return (f, t) switch
        {
            (Pending, InProgress) => true,
            (InProgress, Complete) => true,
            (var ff, var tt) when ff == tt => true,
            _ => false,
        };
    }
}
