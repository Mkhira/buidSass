namespace BackendApi.Modules.Support.Primitives;

public enum TicketMessageKind
{
    CustomerReply = 0,
    AgentReply = 1,
    InternalNote = 2,
    SystemEvent = 3,
}

public static class TicketMessageKindNames
{
    public const string CustomerReply = "customer_reply";
    public const string AgentReply = "agent_reply";
    public const string InternalNote = "internal_note";
    public const string SystemEvent = "system_event";

    public static readonly string[] All = { CustomerReply, AgentReply, InternalNote, SystemEvent };
    public static readonly string[] CustomerVisible = { CustomerReply, AgentReply, SystemEvent };

    public static string ToWire(TicketMessageKind k) => k switch
    {
        TicketMessageKind.CustomerReply => CustomerReply,
        TicketMessageKind.AgentReply => AgentReply,
        TicketMessageKind.InternalNote => InternalNote,
        TicketMessageKind.SystemEvent => SystemEvent,
        _ => throw new InvalidOperationException($"Unknown TicketMessageKind: {k}"),
    };

    public static TicketMessageKind FromWire(string s) => s switch
    {
        CustomerReply => TicketMessageKind.CustomerReply,
        AgentReply => TicketMessageKind.AgentReply,
        InternalNote => TicketMessageKind.InternalNote,
        SystemEvent => TicketMessageKind.SystemEvent,
        _ => throw new InvalidOperationException($"Unknown TicketMessageKind wire value: '{s}'."),
    };
}
