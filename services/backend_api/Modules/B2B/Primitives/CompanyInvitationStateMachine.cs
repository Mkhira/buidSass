namespace BackendApi.Modules.B2B.Primitives;

/// <summary>
/// Spec 021 invitation state machine (data-model §3.2). Pure function. Pending is the
/// only non-terminal state; no terminal-to-terminal moves; no terminal-to-pending.
/// </summary>
public static class CompanyInvitationStateMachine
{
    /// <summary>
    /// Returns <c>true</c> when the move from <paramref name="from"/> to <paramref name="to"/>
    /// is permitted in the abstract. The "revoked by company-admin" case is modeled as a
    /// `pending → expired` transition with audit metadata (`revoked_by`); the state machine
    /// itself does not distinguish them.
    /// </summary>
    public static bool CanTransition(CompanyInvitationState from, CompanyInvitationState to)
    {
        if (from.IsTerminal())
        {
            return false;
        }

        if (from == to)
        {
            return false;
        }

        return (from, to) switch
        {
            (CompanyInvitationState.Pending, CompanyInvitationState.Accepted) => true,
            (CompanyInvitationState.Pending, CompanyInvitationState.Declined) => true,
            (CompanyInvitationState.Pending, CompanyInvitationState.Expired) => true,
            _ => false,
        };
    }
}
