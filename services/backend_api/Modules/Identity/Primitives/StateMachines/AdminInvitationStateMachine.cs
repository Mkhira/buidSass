namespace BackendApi.Modules.Identity.Primitives.StateMachines;

public sealed class AdminInvitationStateMachine
{
    private static readonly IReadOnlyDictionary<(AdminInvitationState State, AdminInvitationTrigger Trigger), AdminInvitationState> Transitions =
        new Dictionary<(AdminInvitationState, AdminInvitationTrigger), AdminInvitationState>
        {
            [(AdminInvitationState.Pending, AdminInvitationTrigger.Accept)] = AdminInvitationState.Accepted,
            [(AdminInvitationState.Pending, AdminInvitationTrigger.Revoke)] = AdminInvitationState.Revoked,
            [(AdminInvitationState.Pending, AdminInvitationTrigger.Expires)] = AdminInvitationState.Expired,
        };

    public bool TryTransition(
        AdminInvitationState state,
        AdminInvitationTrigger trigger,
        out AdminInvitationState nextState)
    {
        if (Transitions.TryGetValue((state, trigger), out nextState))
        {
            return true;
        }

        nextState = state;
        return false;
    }
}

public enum AdminInvitationState
{
    Pending = 0,
    Accepted = 1,
    Revoked = 2,
    Expired = 3,
}

public enum AdminInvitationTrigger
{
    Accept = 0,
    Revoke = 1,
    Expires = 2,
}
