namespace BackendApi.Modules.Identity.Primitives.StateMachines;

public sealed class RefreshTokenStateMachine
{
    private static readonly IReadOnlyDictionary<(RefreshTokenState State, RefreshTokenTrigger Trigger), RefreshTokenState> Transitions =
        new Dictionary<(RefreshTokenState, RefreshTokenTrigger), RefreshTokenState>
        {
            [(RefreshTokenState.Active, RefreshTokenTrigger.Consume)] = RefreshTokenState.Consumed,
            [(RefreshTokenState.Active, RefreshTokenTrigger.Revoke)] = RefreshTokenState.Revoked,
            [(RefreshTokenState.Active, RefreshTokenTrigger.Expire)] = RefreshTokenState.Expired,
            [(RefreshTokenState.Consumed, RefreshTokenTrigger.Revoke)] = RefreshTokenState.Revoked,
        };

    public bool TryTransition(RefreshTokenState state, RefreshTokenTrigger trigger, out RefreshTokenState nextState)
    {
        if (Transitions.TryGetValue((state, trigger), out nextState))
        {
            return true;
        }

        nextState = state;
        return false;
    }
}

public enum RefreshTokenState
{
    Active = 0,
    Consumed = 1,
    Revoked = 2,
    Expired = 3,
}

public enum RefreshTokenTrigger
{
    Consume = 0,
    Revoke = 1,
    Expire = 2,
}
