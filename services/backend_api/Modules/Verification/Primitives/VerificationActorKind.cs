namespace BackendApi.Modules.Verification.Primitives;

/// <summary>
/// Who performed a state transition. Stored as snake_case text on
/// <c>verification_state_transitions.actor_kind</c>.
/// </summary>
public enum VerificationActorKind
{
    Customer,
    Reviewer,
    System,
}

public static class VerificationActorKindExtensions
{
    public static string ToWireValue(this VerificationActorKind kind) => kind switch
    {
        VerificationActorKind.Customer => "customer",
        VerificationActorKind.Reviewer => "reviewer",
        VerificationActorKind.System => "system",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static bool TryParseWireValue(string? wire, out VerificationActorKind kind)
    {
        switch (wire)
        {
            case "customer": kind = VerificationActorKind.Customer; return true;
            case "reviewer": kind = VerificationActorKind.Reviewer; return true;
            case "system": kind = VerificationActorKind.System; return true;
            default: kind = default; return false;
        }
    }
}
