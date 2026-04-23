namespace BackendApi.Modules.Identity.Primitives.StateMachines;

public sealed class SessionStateMachine
{
    private static readonly IReadOnlyDictionary<(SessionState State, SessionTrigger Trigger), SessionState> Transitions =
        new Dictionary<(SessionState, SessionTrigger), SessionState>
        {
            [(SessionState.Active, SessionTrigger.Revoke)] = SessionState.Revoked,
            [(SessionState.Active, SessionTrigger.Expire)] = SessionState.Expired,
        };

    public bool TryTransition(SessionState state, SessionTrigger trigger, out SessionState nextState)
    {
        if (Transitions.TryGetValue((state, trigger), out nextState))
        {
            return true;
        }

        nextState = state;
        return false;
    }
}

public enum SessionState
{
    Active = 0,
    Revoked = 1,
    Expired = 2,
}

public enum SessionTrigger
{
    Revoke = 0,
    Expire = 1,
}
