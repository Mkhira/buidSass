namespace BackendApi.Modules.Support.Primitives;

/// <summary>
/// Compile-time guarded transitions for the 5-state ticket lifecycle.
/// Reopen edge <c>Resolved → InProgress</c> is the ONE non-monotonic
/// transition; closed is terminal.
/// </summary>
public static class TicketStateMachine
{
    /// <summary>
    /// Validates a proposed transition. Idempotent (from == to) returns true
    /// so callers can short-circuit without writing audit; the only legal
    /// non-creation entry to <c>Open</c> is creation itself (no other source
    /// state transitions <i>back</i> to <c>Open</c>).
    /// </summary>
    public static bool TryTransition(
        TicketState from,
        TicketState to,
        string trigger,
        TicketActorKind actor,
        out string? reasonCode)
    {
        reasonCode = null;

        if (from == TicketState.Closed)
        {
            reasonCode = TicketReasonCode.ClosedTerminal;
            return false;
        }

        if (from == to)
        {
            return true;
        }

        if (Array.IndexOf(TicketTriggerKind.All, trigger) < 0)
        {
            reasonCode = TicketReasonCode.InvalidTransition;
            return false;
        }

        switch ((from, to))
        {
            // open → in_progress (claim / auto-assign / lead-assignment)
            case (TicketState.Open, TicketState.InProgress)
                when trigger is TicketTriggerKind.AgentClaim or TicketTriggerKind.AgentAssignment
                  && actor is TicketActorKind.Agent or TicketActorKind.Lead or TicketActorKind.System:
                return true;

            // in_progress → waiting_customer (agent reply asks for info)
            case (TicketState.InProgress, TicketState.WaitingCustomer)
                when trigger == TicketTriggerKind.AgentReply
                  && actor is TicketActorKind.Agent or TicketActorKind.Lead or TicketActorKind.SuperAdmin or TicketActorKind.System:
                return true;

            // waiting_customer → in_progress (customer reply)
            case (TicketState.WaitingCustomer, TicketState.InProgress)
                when trigger == TicketTriggerKind.CustomerReply
                  && actor == TicketActorKind.Customer:
                return true;

            // open → in_progress (customer reply on freshly-opened ticket)
            case (TicketState.Open, TicketState.InProgress)
                when trigger == TicketTriggerKind.CustomerReply
                  && actor == TicketActorKind.Customer:
                return true;

            // in_progress → resolved (agent resolves)
            case (TicketState.InProgress, TicketState.Resolved)
                when trigger is TicketTriggerKind.AgentResolve or TicketTriggerKind.ReturnOutcome
                  && actor is TicketActorKind.Agent or TicketActorKind.Lead or TicketActorKind.SuperAdmin or TicketActorKind.System:
                return true;

            // waiting_customer → resolved (return outcome arrives)
            case (TicketState.WaitingCustomer, TicketState.Resolved)
                when trigger == TicketTriggerKind.ReturnOutcome
                  && actor == TicketActorKind.System:
                return true;

            // resolved → in_progress (customer reopen within window)
            case (TicketState.Resolved, TicketState.InProgress)
                when trigger == TicketTriggerKind.CustomerReopen
                  && actor == TicketActorKind.Customer:
                return true;

            // resolved → closed (auto-close / return outcome / lead-force-close)
            case (TicketState.Resolved, TicketState.Closed)
                when trigger is TicketTriggerKind.AutoCloseResolutionWindow
                            or TicketTriggerKind.LeadForceClose
                            or TicketTriggerKind.AuthorAccountLocked
                  && actor is TicketActorKind.System or TicketActorKind.Lead or TicketActorKind.SuperAdmin:
                return true;

            // any non-closed → closed via lead force-close OR account-locked cascade
            case (_, TicketState.Closed)
                when trigger is TicketTriggerKind.LeadForceClose or TicketTriggerKind.AuthorAccountLocked
                  && actor is TicketActorKind.Lead or TicketActorKind.SuperAdmin or TicketActorKind.System:
                return true;

            default:
                reasonCode = TicketReasonCode.InvalidTransition;
                return false;
        }
    }

    /// <summary>True when state is the single terminal value.</summary>
    public static bool IsTerminal(TicketState state) => state == TicketState.Closed;

    /// <summary>True when state may receive a customer reply.</summary>
    public static bool AcceptsCustomerReply(TicketState state) =>
        state is TicketState.Open or TicketState.InProgress or TicketState.WaitingCustomer;
}
