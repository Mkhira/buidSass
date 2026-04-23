namespace BackendApi.Modules.Identity.Primitives.StateMachines;

public sealed class AccountStateMachine
{
    private static readonly IReadOnlyDictionary<(AccountState State, AccountTrigger Trigger), AccountState> Transitions =
        new Dictionary<(AccountState, AccountTrigger), AccountState>
        {
            [(AccountState.PendingEmailVerification, AccountTrigger.ConfirmEmail)] = AccountState.Active,
            [(AccountState.PendingEmailVerification, AccountTrigger.Delete)] = AccountState.Deleted,
            [(AccountState.PendingPasswordRotation, AccountTrigger.CompletePasswordRotation)] = AccountState.Active,
            [(AccountState.Active, AccountTrigger.Lock)] = AccountState.Locked,
            [(AccountState.Active, AccountTrigger.Disable)] = AccountState.Disabled,
            [(AccountState.Active, AccountTrigger.RequirePasswordRotation)] = AccountState.PendingPasswordRotation,
            [(AccountState.Active, AccountTrigger.Delete)] = AccountState.Deleted,
            [(AccountState.PendingPasswordRotation, AccountTrigger.Disable)] = AccountState.Disabled,
            [(AccountState.PendingPasswordRotation, AccountTrigger.Delete)] = AccountState.Deleted,
            [(AccountState.Locked, AccountTrigger.Unlock)] = AccountState.Active,
            [(AccountState.Locked, AccountTrigger.Disable)] = AccountState.Disabled,
            [(AccountState.Locked, AccountTrigger.Delete)] = AccountState.Deleted,
            [(AccountState.Disabled, AccountTrigger.Enable)] = AccountState.Active,
            [(AccountState.Disabled, AccountTrigger.Delete)] = AccountState.Deleted,
        };

    public bool TryTransition(AccountState state, AccountTrigger trigger, out AccountState nextState)
    {
        if (Transitions.TryGetValue((state, trigger), out nextState))
        {
            return true;
        }

        nextState = state;
        return false;
    }
}

public enum AccountState
{
    PendingEmailVerification = 0,
    PendingPasswordRotation = 1,
    Active = 2,
    Locked = 3,
    Disabled = 4,
    Deleted = 5,
}

public enum AccountTrigger
{
    ConfirmEmail = 0,
    CompletePasswordRotation = 1,
    RequirePasswordRotation = 2,
    Lock = 3,
    Unlock = 4,
    Disable = 5,
    Enable = 6,
    Delete = 7,
}
