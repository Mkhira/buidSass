namespace BackendApi.Modules.Identity.Primitives.StateMachines;

public sealed class IdentityLockoutStateMachine
{
    private static readonly IReadOnlyDictionary<(IdentityLockoutState State, IdentityLockoutTrigger Trigger), IdentityLockoutState> Transitions =
        new Dictionary<(IdentityLockoutState, IdentityLockoutTrigger), IdentityLockoutState>
        {
            [(IdentityLockoutState.Clear, IdentityLockoutTrigger.RegisterFailure)] = IdentityLockoutState.Tracking,
            [(IdentityLockoutState.Tracking, IdentityLockoutTrigger.RegisterFailure)] = IdentityLockoutState.Tracking,
            [(IdentityLockoutState.Tracking, IdentityLockoutTrigger.ThresholdReached)] = IdentityLockoutState.Locked,
            [(IdentityLockoutState.Tracking, IdentityLockoutTrigger.Reset)] = IdentityLockoutState.Clear,
            [(IdentityLockoutState.Locked, IdentityLockoutTrigger.WindowElapsed)] = IdentityLockoutState.Clear,
            [(IdentityLockoutState.Locked, IdentityLockoutTrigger.Reset)] = IdentityLockoutState.Clear,
        };

    public bool TryTransition(
        IdentityLockoutState state,
        IdentityLockoutTrigger trigger,
        out IdentityLockoutState nextState)
    {
        if (Transitions.TryGetValue((state, trigger), out nextState))
        {
            return true;
        }

        nextState = state;
        return false;
    }
}

public enum IdentityLockoutState
{
    Clear = 0,
    Tracking = 1,
    Locked = 2,
}

public enum IdentityLockoutTrigger
{
    RegisterFailure = 0,
    ThresholdReached = 1,
    WindowElapsed = 2,
    Reset = 3,
}
