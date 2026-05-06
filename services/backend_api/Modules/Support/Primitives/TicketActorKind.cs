namespace BackendApi.Modules.Support.Primitives;

public enum TicketActorKind
{
    Customer = 0,
    Agent = 1,
    Lead = 2,
    SuperAdmin = 3,
    FinanceViewer = 4,
    ReviewModerator = 5,
    B2BAccountManager = 6,
    System = 7,
}

public static class TicketActorKindNames
{
    public const string Customer = "customer";
    public const string Agent = "support.agent";
    public const string Lead = "support.lead";
    public const string SuperAdmin = "super_admin";
    public const string FinanceViewer = "viewer.finance";
    public const string ReviewModerator = "reviews.moderator";
    public const string B2BAccountManager = "b2b.account_manager";
    public const string System = "system";

    public static string ToWire(TicketActorKind a) => a switch
    {
        TicketActorKind.Customer => Customer,
        TicketActorKind.Agent => Agent,
        TicketActorKind.Lead => Lead,
        TicketActorKind.SuperAdmin => SuperAdmin,
        TicketActorKind.FinanceViewer => FinanceViewer,
        TicketActorKind.ReviewModerator => ReviewModerator,
        TicketActorKind.B2BAccountManager => B2BAccountManager,
        TicketActorKind.System => System,
        _ => throw new InvalidOperationException($"Unknown TicketActorKind: {a}"),
    };
}
