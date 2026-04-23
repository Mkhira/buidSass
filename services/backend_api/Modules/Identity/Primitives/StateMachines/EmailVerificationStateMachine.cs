namespace BackendApi.Modules.Identity.Primitives.StateMachines;

public sealed class EmailVerificationStateMachine
{
    private static readonly IReadOnlyDictionary<(EmailVerificationState State, EmailVerificationTrigger Trigger), EmailVerificationState> Transitions =
        new Dictionary<(EmailVerificationState, EmailVerificationTrigger), EmailVerificationState>
        {
            [(EmailVerificationState.Pending, EmailVerificationTrigger.Confirm)] = EmailVerificationState.Completed,
            [(EmailVerificationState.Pending, EmailVerificationTrigger.Expires)] = EmailVerificationState.Expired,
            [(EmailVerificationState.Pending, EmailVerificationTrigger.Consume)] = EmailVerificationState.Consumed,
        };

    public bool TryTransition(
        EmailVerificationState state,
        EmailVerificationTrigger trigger,
        out EmailVerificationState nextState)
    {
        if (Transitions.TryGetValue((state, trigger), out nextState))
        {
            return true;
        }

        nextState = state;
        return false;
    }
}

public enum EmailVerificationState
{
    Pending = 0,
    Completed = 1,
    Expired = 2,
    Consumed = 3,
}

public enum EmailVerificationTrigger
{
    Confirm = 0,
    Expires = 1,
    Consume = 2,
}
