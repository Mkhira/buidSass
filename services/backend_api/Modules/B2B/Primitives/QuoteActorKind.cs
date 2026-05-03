namespace BackendApi.Modules.B2B.Primitives;

/// <summary>
/// Spec 021 actor classification on every quote state transition (data-model §2.8).
///
/// Persisted as a lowercase-string token on <c>quote_state_transitions.actor_kind</c>.
/// The <see cref="QuoteActorKindExtensions.ToToken"/> seam is the single source of truth.
/// </summary>
public enum QuoteActorKind
{
    /// <summary>Individual end-customer (no company-account).</summary>
    Customer = 0,

    /// <summary>Company-account member with the <c>buyer</c> membership role.</summary>
    Buyer = 1,

    /// <summary>Company-account member with the <c>approver</c> membership role.</summary>
    Approver = 2,

    /// <summary>Admin user with <c>quotes.author</c> permission (admin authoring slice).</summary>
    AdminOperator = 3,

    /// <summary>Background worker / lifecycle hook acting on behalf of the platform.</summary>
    System = 4,
}

public static class QuoteActorKindExtensions
{
    public static string ToToken(this QuoteActorKind actor) => actor switch
    {
        QuoteActorKind.Customer => "customer",
        QuoteActorKind.Buyer => "buyer",
        QuoteActorKind.Approver => "approver",
        QuoteActorKind.AdminOperator => "admin_operator",
        QuoteActorKind.System => "system",
        _ => throw new ArgumentOutOfRangeException(nameof(actor), actor, "Unknown QuoteActorKind"),
    };

    public static bool TryParseToken(string token, out QuoteActorKind actor)
    {
        switch (token)
        {
            case "customer": actor = QuoteActorKind.Customer; return true;
            case "buyer": actor = QuoteActorKind.Buyer; return true;
            case "approver": actor = QuoteActorKind.Approver; return true;
            case "admin_operator": actor = QuoteActorKind.AdminOperator; return true;
            case "system": actor = QuoteActorKind.System; return true;
            default: actor = default; return false;
        }
    }
}
