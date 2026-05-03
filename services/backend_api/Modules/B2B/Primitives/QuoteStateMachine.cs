namespace BackendApi.Modules.B2B.Primitives;

/// <summary>
/// Spec 021 quote state machine (data-model §3.1). Pure function, no DI; decisions are
/// local to (from, to, actorKind). Handlers MUST consult <see cref="CanTransition"/>
/// before writing a new state and MUST NOT branch on <see cref="QuoteState"/> directly
/// for transition validity.
///
/// Forbidden transitions (data-model §3.1 "Forbidden"):
/// <list type="bullet">
///   <item>Any terminal → non-terminal.</item>
///   <item><c>requested → accepted</c> (must publish first).</item>
///   <item><c>drafted → pending-approver</c> (drafted is operator-invisible to customers).</item>
/// </list>
/// </summary>
public static class QuoteStateMachine
{
    /// <summary>
    /// Returns <c>true</c> when an actor of the given kind may move a quote from
    /// <paramref name="from"/> to <paramref name="to"/> in the absence of any other guard.
    /// Per-handler guards (rate-limit, eligibility, idempotency, row-version) are NOT
    /// modelled here — they live in the slice handlers because they require I/O.
    /// </summary>
    public static bool CanTransition(QuoteState from, QuoteState to, QuoteActorKind actorKind)
    {
        // Terminal → anything is forbidden. Reopen happens by creating a new quote.
        if (from.IsTerminal())
        {
            return false;
        }

        // No-op self-transitions are not legal — the audit ledger is built off real state
        // changes, and `prior_state == new_state` would corrupt the timeline.
        if (from == to)
        {
            return false;
        }

        return (from, to) switch
        {
            // Customer / buyer rejects or withdraws from any non-terminal state.
            (_, QuoteState.Rejected) when actorKind is QuoteActorKind.Customer or QuoteActorKind.Buyer => true,
            (_, QuoteState.Withdrawn) when actorKind is QuoteActorKind.Customer or QuoteActorKind.Buyer => true,

            // System-driven voids on account-lifecycle events.
            (_, QuoteState.Withdrawn) when actorKind is QuoteActorKind.System => true,

            // Worker-driven expiry from any non-terminal state past expires_at.
            (_, QuoteState.Expired) when actorKind is QuoteActorKind.System => true,

            // Admin opens a `requested` quote to author the first version.
            (QuoteState.Requested, QuoteState.Drafted) when actorKind is QuoteActorKind.AdminOperator => true,

            // Admin publishes the draft (first publish or revision).
            (QuoteState.Drafted, QuoteState.Revised) when actorKind is QuoteActorKind.AdminOperator => true,

            // Customer/buyer requests a revision, OR admin proactively re-opens to revise.
            (QuoteState.Revised, QuoteState.Drafted) when actorKind is
                QuoteActorKind.Customer or QuoteActorKind.Buyer or QuoteActorKind.AdminOperator => true,

            // Buyer submits acceptance — routed to approver when company.approver_required=true.
            // The handler chooses between this transition and `revised → accepted` based on
            // the company's policy + approver availability; the state machine permits both.
            (QuoteState.Revised, QuoteState.PendingApprover) when actorKind is QuoteActorKind.Buyer => true,

            // Buyer/customer submits acceptance with no approver required.
            (QuoteState.Revised, QuoteState.Accepted) when actorKind is
                QuoteActorKind.Buyer or QuoteActorKind.Customer => true,

            // Approver finalizes (the first concurrent finalize wins via xmin guard).
            (QuoteState.PendingApprover, QuoteState.Accepted) when actorKind is QuoteActorKind.Approver => true,

            // Approver rejects with a comment → returns to revised so admin/customer can iterate.
            (QuoteState.PendingApprover, QuoteState.Revised) when actorKind is QuoteActorKind.Approver => true,

            // System rolls a pending-approver quote back when the only approver leaves the company.
            (QuoteState.PendingApprover, QuoteState.Revised) when actorKind is QuoteActorKind.System => true,

            // Forbidden by data-model §3.1: requested → accepted directly,
            // drafted → pending-approver directly, anything else not listed.
            _ => false,
        };
    }
}
