namespace BackendApi.Modules.Identity.Primitives.StateMachines;

public sealed class OtpChallengeStateMachine
{
    private static readonly IReadOnlyDictionary<(OtpChallengeState State, OtpChallengeTrigger Trigger), OtpChallengeState> Transitions =
        new Dictionary<(OtpChallengeState, OtpChallengeTrigger), OtpChallengeState>
        {
            [(OtpChallengeState.Pending, OtpChallengeTrigger.VerifySuccess)] = OtpChallengeState.Completed,
            [(OtpChallengeState.Pending, OtpChallengeTrigger.Expires)] = OtpChallengeState.Expired,
            [(OtpChallengeState.Pending, OtpChallengeTrigger.MaxAttemptsReached)] = OtpChallengeState.Exhausted,
            [(OtpChallengeState.Pending, OtpChallengeTrigger.Revoke)] = OtpChallengeState.Revoked,
        };

    public bool TryTransition(OtpChallengeState state, OtpChallengeTrigger trigger, out OtpChallengeState nextState)
    {
        if (Transitions.TryGetValue((state, trigger), out nextState))
        {
            return true;
        }

        nextState = state;
        return false;
    }
}

public enum OtpChallengeState
{
    Pending = 0,
    Completed = 1,
    Expired = 2,
    Exhausted = 3,
    Revoked = 4,
}

public enum OtpChallengeTrigger
{
    VerifySuccess = 0,
    Expires = 1,
    MaxAttemptsReached = 2,
    Revoke = 3,
}
