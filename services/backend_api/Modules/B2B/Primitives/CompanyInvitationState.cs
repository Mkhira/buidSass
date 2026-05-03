namespace BackendApi.Modules.B2B.Primitives;

/// <summary>
/// Spec 021 company-invitation lifecycle (data-model §3.2). One inbound `pending` state and
/// three terminal states — invitations cannot be resurrected from terminal.
///
/// Token round-trip via <see cref="CompanyInvitationStateExtensions"/>; persisted on
/// <c>company_invitations.state</c>.
/// </summary>
public enum CompanyInvitationState
{
    /// <summary>Awaiting invitee response. Non-terminal.</summary>
    Pending = 0,

    /// <summary>Invitee created the membership. Terminal.</summary>
    Accepted = 1,

    /// <summary>Invitee explicitly declined. Terminal.</summary>
    Declined = 2,

    /// <summary>Past <c>expires_at</c> (worker), or revoked by company-admin (audit metadata flags this). Terminal.</summary>
    Expired = 3,
}

public static class CompanyInvitationStateExtensions
{
    public static string ToToken(this CompanyInvitationState state) => state switch
    {
        CompanyInvitationState.Pending => "pending",
        CompanyInvitationState.Accepted => "accepted",
        CompanyInvitationState.Declined => "declined",
        CompanyInvitationState.Expired => "expired",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown CompanyInvitationState"),
    };

    public static bool TryParseToken(string token, out CompanyInvitationState state)
    {
        switch (token)
        {
            case "pending": state = CompanyInvitationState.Pending; return true;
            case "accepted": state = CompanyInvitationState.Accepted; return true;
            case "declined": state = CompanyInvitationState.Declined; return true;
            case "expired": state = CompanyInvitationState.Expired; return true;
            default: state = default; return false;
        }
    }

    public static bool IsTerminal(this CompanyInvitationState state) => state is
        CompanyInvitationState.Accepted or
        CompanyInvitationState.Declined or
        CompanyInvitationState.Expired;
}
