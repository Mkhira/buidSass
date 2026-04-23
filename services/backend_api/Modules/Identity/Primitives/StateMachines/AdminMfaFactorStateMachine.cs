namespace BackendApi.Modules.Identity.Primitives.StateMachines;

public sealed class AdminMfaFactorStateMachine
{
    private static readonly IReadOnlyDictionary<(AdminMfaFactorState State, AdminMfaFactorTrigger Trigger), AdminMfaFactorState> Transitions =
        new Dictionary<(AdminMfaFactorState, AdminMfaFactorTrigger), AdminMfaFactorState>
        {
            [(AdminMfaFactorState.PendingConfirmation, AdminMfaFactorTrigger.Confirm)] = AdminMfaFactorState.Active,
            [(AdminMfaFactorState.PendingConfirmation, AdminMfaFactorTrigger.Revoke)] = AdminMfaFactorState.Revoked,
            [(AdminMfaFactorState.Active, AdminMfaFactorTrigger.StartRotation)] = AdminMfaFactorState.Rotating,
            [(AdminMfaFactorState.Active, AdminMfaFactorTrigger.Revoke)] = AdminMfaFactorState.Revoked,
            [(AdminMfaFactorState.Rotating, AdminMfaFactorTrigger.Confirm)] = AdminMfaFactorState.Active,
            [(AdminMfaFactorState.Rotating, AdminMfaFactorTrigger.Revoke)] = AdminMfaFactorState.Revoked,
        };

    public bool TryTransition(
        AdminMfaFactorState state,
        AdminMfaFactorTrigger trigger,
        out AdminMfaFactorState nextState)
    {
        if (Transitions.TryGetValue((state, trigger), out nextState))
        {
            return true;
        }

        nextState = state;
        return false;
    }
}

public enum AdminMfaFactorState
{
    PendingConfirmation = 0,
    Active = 1,
    Rotating = 2,
    Revoked = 3,
}

public enum AdminMfaFactorTrigger
{
    Confirm = 0,
    StartRotation = 1,
    Revoke = 2,
}
