namespace BackendApi.Modules.Support.Primitives;

public enum TicketPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3,
}

public static class TicketPriorityNames
{
    public const string Low = "low";
    public const string Normal = "normal";
    public const string High = "high";
    public const string Urgent = "urgent";

    public static readonly string[] All = { Low, Normal, High, Urgent };

    /// <summary>FR-006 — customers may select only low/normal at creation.</summary>
    public static readonly string[] CustomerSelectable = { Low, Normal };

    public static string ToWire(TicketPriority p) => p switch
    {
        TicketPriority.Low => Low,
        TicketPriority.Normal => Normal,
        TicketPriority.High => High,
        TicketPriority.Urgent => Urgent,
        _ => throw new InvalidOperationException($"Unknown TicketPriority: {p}"),
    };

    public static TicketPriority FromWire(string s) => s switch
    {
        Low => TicketPriority.Low,
        Normal => TicketPriority.Normal,
        High => TicketPriority.High,
        Urgent => TicketPriority.Urgent,
        _ => throw new InvalidOperationException($"Unknown TicketPriority wire value: '{s}'."),
    };
}
