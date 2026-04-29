namespace BackendApi.Modules.Verification.Primitives;

/// <summary>
/// Verification lifecycle state per spec 020 data-model §3.
/// Persisted as a snake_case text discriminator in the <c>verifications.state</c>
/// column. The mapper to/from text lives next to the EF configuration so the
/// CHECK constraint values match exactly.
/// </summary>
public enum VerificationState
{
    Submitted,
    InReview,
    InfoRequested,
    Approved,
    Rejected,
    Expired,
    Revoked,
    Superseded,
    Void,
}

public static class VerificationStateExtensions
{
    /// <summary>
    /// Wire-format value used by the CHECK constraint and the
    /// <c>verification_state_transitions</c> append-only ledger.
    /// </summary>
    public static string ToWireValue(this VerificationState state) => state switch
    {
        VerificationState.Submitted => "submitted",
        VerificationState.InReview => "in-review",
        VerificationState.InfoRequested => "info-requested",
        VerificationState.Approved => "approved",
        VerificationState.Rejected => "rejected",
        VerificationState.Expired => "expired",
        VerificationState.Revoked => "revoked",
        VerificationState.Superseded => "superseded",
        VerificationState.Void => "void",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static bool TryParseWireValue(string? wire, out VerificationState state)
    {
        switch (wire)
        {
            case "submitted": state = VerificationState.Submitted; return true;
            case "in-review": state = VerificationState.InReview; return true;
            case "info-requested": state = VerificationState.InfoRequested; return true;
            case "approved": state = VerificationState.Approved; return true;
            case "rejected": state = VerificationState.Rejected; return true;
            case "expired": state = VerificationState.Expired; return true;
            case "revoked": state = VerificationState.Revoked; return true;
            case "superseded": state = VerificationState.Superseded; return true;
            case "void": state = VerificationState.Void; return true;
            default: state = default; return false;
        }
    }

    /// <summary>
    /// Terminal states cannot transition anywhere except via a brand-new row.
    /// `approved` is non-terminal active per data-model §3 (it can move to
    /// `expired`/`revoked`/`superseded`/`void`).
    /// </summary>
    public static bool IsTerminal(this VerificationState state) => state is
        VerificationState.Rejected or
        VerificationState.Expired or
        VerificationState.Revoked or
        VerificationState.Superseded or
        VerificationState.Void;
}
