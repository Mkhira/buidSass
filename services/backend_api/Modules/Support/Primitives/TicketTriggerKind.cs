namespace BackendApi.Modules.Support.Primitives;

/// <summary>13 trigger kinds per data-model §3.</summary>
public static class TicketTriggerKind
{
    public const string CustomerSubmission = "customer_submission";
    public const string AgentClaim = "agent_claim";
    public const string AgentAssignment = "agent_assignment";
    public const string CustomerReply = "customer_reply";
    public const string AgentReply = "agent_reply";
    public const string AgentResolve = "agent_resolve";
    public const string CustomerReopen = "customer_reopen";
    public const string AutoCloseResolutionWindow = "auto_close_resolution_window";
    public const string ReturnOutcome = "return_outcome";
    public const string AuthorAccountLocked = "author_account_locked";
    public const string LeadForceClose = "lead_force_close";
    public const string LeadReassign = "lead_reassign";
    public const string AgentOffboarded = "agent_offboarded";

    public static readonly string[] All =
    {
        CustomerSubmission, AgentClaim, AgentAssignment, CustomerReply, AgentReply,
        AgentResolve, CustomerReopen, AutoCloseResolutionWindow, ReturnOutcome,
        AuthorAccountLocked, LeadForceClose, LeadReassign, AgentOffboarded,
    };
}
