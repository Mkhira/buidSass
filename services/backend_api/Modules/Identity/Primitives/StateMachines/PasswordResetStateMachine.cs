namespace BackendApi.Modules.Identity.Primitives.StateMachines;

public sealed class PasswordResetStateMachine
{
    private static readonly IReadOnlyDictionary<(PasswordResetState State, PasswordResetTrigger Trigger), PasswordResetState> Transitions =
        new Dictionary<(PasswordResetState, PasswordResetTrigger), PasswordResetState>
        {
            [(PasswordResetState.Pending, PasswordResetTrigger.Consume)] = PasswordResetState.Consumed,
            [(PasswordResetState.Pending, PasswordResetTrigger.Expires)] = PasswordResetState.Expired,
            [(PasswordResetState.Pending, PasswordResetTrigger.Revoke)] = PasswordResetState.Revoked,
        };

    public bool TryTransition(PasswordResetState state, PasswordResetTrigger trigger, out PasswordResetState nextState)
    {
        if (Transitions.TryGetValue((state, trigger), out nextState))
        {
            return true;
        }

        nextState = state;
        return false;
    }
}

public enum PasswordResetState
{
    Pending = 0,
    Consumed = 1,
    Expired = 2,
    Revoked = 3,
}

public enum PasswordResetTrigger
{
    Consume = 0,
    Expires = 1,
    Revoke = 2,
}
