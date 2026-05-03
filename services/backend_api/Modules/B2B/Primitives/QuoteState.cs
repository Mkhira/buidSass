namespace BackendApi.Modules.B2B.Primitives;

/// <summary>
/// Spec 021 quote lifecycle states (data-model §3.1).
///
/// Storage: persisted as a stable lowercase-string token (`requested`, `drafted`, ...) on
/// <c>quotes.state</c> and on <c>quote_state_transitions.prior_state / new_state</c>. The
/// <see cref="QuoteStateExtensions.ToToken"/> + <see cref="QuoteStateExtensions.ParseToken"/>
/// pair are the single round-trip seam — handlers and EF configurations MUST use them
/// rather than hand-rolling case statements.
/// </summary>
public enum QuoteState
{
    /// <summary>Customer-submitted; awaiting admin pickup. Non-terminal.</summary>
    Requested = 0,

    /// <summary>Admin operator authoring a version not yet published. Customer-invisible. Non-terminal.</summary>
    Drafted = 1,

    /// <summary>At least one version published; default state between revisions. Customer-visible. Non-terminal.</summary>
    Revised = 2,

    /// <summary>Buyer submitted acceptance; one of N approvers can finalize or reject. Non-terminal.</summary>
    PendingApprover = 3,

    /// <summary>Order created. Terminal.</summary>
    Accepted = 4,

    /// <summary>Customer-rejected. No cool-down (research §R10). Terminal.</summary>
    Rejected = 5,

    /// <summary>Past <c>expires_at</c>. Terminal.</summary>
    Expired = 6,

    /// <summary>Customer withdrew or system voided (account-lifecycle). Terminal.</summary>
    Withdrawn = 7,
}

public static class QuoteStateExtensions
{
    /// <summary>
    /// Stable lowercase-string token used in DB storage, contracts, and audit payloads.
    /// Adding a new state here without bumping the schema_version on every existing
    /// quote is a breaking change.
    /// </summary>
    public static string ToToken(this QuoteState state) => state switch
    {
        QuoteState.Requested => "requested",
        QuoteState.Drafted => "drafted",
        QuoteState.Revised => "revised",
        QuoteState.PendingApprover => "pending-approver",
        QuoteState.Accepted => "accepted",
        QuoteState.Rejected => "rejected",
        QuoteState.Expired => "expired",
        QuoteState.Withdrawn => "withdrawn",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown QuoteState"),
    };

    public static bool TryParseToken(string token, out QuoteState state)
    {
        switch (token)
        {
            case "requested": state = QuoteState.Requested; return true;
            case "drafted": state = QuoteState.Drafted; return true;
            case "revised": state = QuoteState.Revised; return true;
            case "pending-approver": state = QuoteState.PendingApprover; return true;
            case "accepted": state = QuoteState.Accepted; return true;
            case "rejected": state = QuoteState.Rejected; return true;
            case "expired": state = QuoteState.Expired; return true;
            case "withdrawn": state = QuoteState.Withdrawn; return true;
            default: state = default; return false;
        }
    }

    public static QuoteState ParseToken(string token) =>
        TryParseToken(token, out var s)
            ? s
            : throw new ArgumentException($"Unknown QuoteState token '{token}'.", nameof(token));

    /// <summary>True when the state cannot transition to any other state via normal handler flow.</summary>
    public static bool IsTerminal(this QuoteState state) => state is
        QuoteState.Accepted or
        QuoteState.Rejected or
        QuoteState.Expired or
        QuoteState.Withdrawn;
}
