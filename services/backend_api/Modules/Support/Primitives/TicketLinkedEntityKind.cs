namespace BackendApi.Modules.Support.Primitives;

public enum TicketLinkedEntityKind
{
    Order = 0,
    OrderLine = 1,
    ReturnRequest = 2,
    Quote = 3,
    Review = 4,
    Verification = 5,
}

public static class TicketLinkedEntityKindNames
{
    public const string Order = "order";
    public const string OrderLine = "order_line";
    public const string ReturnRequest = "return_request";
    public const string Quote = "quote";
    public const string Review = "review";
    public const string Verification = "verification";

    public static readonly string[] All = { Order, OrderLine, ReturnRequest, Quote, Review, Verification };

    public static string ToWire(TicketLinkedEntityKind k) => k switch
    {
        TicketLinkedEntityKind.Order => Order,
        TicketLinkedEntityKind.OrderLine => OrderLine,
        TicketLinkedEntityKind.ReturnRequest => ReturnRequest,
        TicketLinkedEntityKind.Quote => Quote,
        TicketLinkedEntityKind.Review => Review,
        TicketLinkedEntityKind.Verification => Verification,
        _ => throw new InvalidOperationException($"Unknown TicketLinkedEntityKind: {k}"),
    };

    public static TicketLinkedEntityKind FromWire(string s) => s switch
    {
        Order => TicketLinkedEntityKind.Order,
        OrderLine => TicketLinkedEntityKind.OrderLine,
        ReturnRequest => TicketLinkedEntityKind.ReturnRequest,
        Quote => TicketLinkedEntityKind.Quote,
        Review => TicketLinkedEntityKind.Review,
        Verification => TicketLinkedEntityKind.Verification,
        _ => throw new InvalidOperationException($"Unknown TicketLinkedEntityKind wire value: '{s}'."),
    };
}
