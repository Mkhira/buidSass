namespace BackendApi.Modules.Support.Primitives;

/// <summary>
/// 5-state lifecycle per spec 023 data-model §3.
/// </summary>
public enum TicketState
{
    Open = 0,
    InProgress = 1,
    WaitingCustomer = 2,
    Resolved = 3,
    Closed = 4,
}

/// <summary>Wire-string mapping (text + CHECK constraint pattern).</summary>
public static class TicketStateNames
{
    public const string Open = "open";
    public const string InProgress = "in_progress";
    public const string WaitingCustomer = "waiting_customer";
    public const string Resolved = "resolved";
    public const string Closed = "closed";

    public static string ToWire(TicketState s) => s switch
    {
        TicketState.Open => Open,
        TicketState.InProgress => InProgress,
        TicketState.WaitingCustomer => WaitingCustomer,
        TicketState.Resolved => Resolved,
        TicketState.Closed => Closed,
        _ => throw new InvalidOperationException($"Unknown TicketState: {s}"),
    };

    public static TicketState FromWire(string s) => s switch
    {
        Open => TicketState.Open,
        InProgress => TicketState.InProgress,
        WaitingCustomer => TicketState.WaitingCustomer,
        Resolved => TicketState.Resolved,
        Closed => TicketState.Closed,
        _ => throw new InvalidOperationException($"Unknown TicketState wire value: '{s}'."),
    };
}
