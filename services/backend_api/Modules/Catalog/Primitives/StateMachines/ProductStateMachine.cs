namespace BackendApi.Modules.Catalog.Primitives.StateMachines;

public enum ProductState
{
    Draft = 0,
    InReview = 1,
    Scheduled = 2,
    Published = 3,
    Archived = 4,
}

public enum ProductTrigger
{
    Submit = 0,
    Withdraw = 1,
    Publish = 2,
    PublishWithFutureAt = 3,
    WorkerFire = 4,
    CancelSchedule = 5,
    Archive = 6,
    Unarchive = 7,
}

public sealed class ProductStateMachine
{
    private static readonly Dictionary<(ProductState, ProductTrigger), ProductState> Transitions = new()
    {
        [(ProductState.Draft, ProductTrigger.Submit)] = ProductState.InReview,
        [(ProductState.InReview, ProductTrigger.Withdraw)] = ProductState.Draft,
        [(ProductState.InReview, ProductTrigger.Publish)] = ProductState.Published,
        [(ProductState.InReview, ProductTrigger.PublishWithFutureAt)] = ProductState.Scheduled,
        [(ProductState.Scheduled, ProductTrigger.WorkerFire)] = ProductState.Published,
        [(ProductState.Scheduled, ProductTrigger.CancelSchedule)] = ProductState.InReview,
        [(ProductState.Published, ProductTrigger.Archive)] = ProductState.Archived,
        [(ProductState.Archived, ProductTrigger.Unarchive)] = ProductState.Draft,
    };

    public bool TryTransition(ProductState from, ProductTrigger trigger, out ProductState next)
    {
        if (Transitions.TryGetValue((from, trigger), out var to))
        {
            next = to;
            return true;
        }

        next = from;
        return false;
    }

    public static string Encode(ProductState state) => state switch
    {
        ProductState.Draft => "draft",
        ProductState.InReview => "in_review",
        ProductState.Scheduled => "scheduled",
        ProductState.Published => "published",
        ProductState.Archived => "archived",
        _ => "draft",
    };

    public static bool TryParse(string value, out ProductState state)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "draft":
                state = ProductState.Draft;
                return true;
            case "in_review":
                state = ProductState.InReview;
                return true;
            case "scheduled":
                state = ProductState.Scheduled;
                return true;
            case "published":
                state = ProductState.Published;
                return true;
            case "archived":
                state = ProductState.Archived;
                return true;
            default:
                state = ProductState.Draft;
                return false;
        }
    }
}
