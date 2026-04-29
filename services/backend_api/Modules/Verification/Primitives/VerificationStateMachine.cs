namespace BackendApi.Modules.Verification.Primitives;

/// <summary>
/// Transition table for <see cref="VerificationState"/> per spec 020 data-model §3.2.
/// Pure logic — no DI, no I/O. Handlers call <see cref="CanTransition"/> before
/// applying the new state and <see cref="EnsureCanTransitionOrThrow"/> when the
/// failure should surface as an exception (caught by the global handler and mapped
/// to <see cref="VerificationReasonCode.InvalidStateForAction"/>).
/// </summary>
public static class VerificationStateMachine
{
    /// <summary>
    /// Sentinel "no prior state" used by the <c>verification_state_transitions</c>
    /// initial-submission row (see data-model §2.3).
    /// </summary>
    public const string PriorStateNoneWire = "__none__";

    /// <summary>
    /// True if the (from → to) edge is allowed for the given actor. Forbidden edges
    /// per data-model §3.2:
    /// <list type="bullet">
    ///   <item>terminal → non-terminal,</item>
    ///   <item>any → submitted (re-submission requires a new row),</item>
    ///   <item>info-requested → approved/rejected/revoked direct (must round-trip via in-review).</item>
    /// </list>
    /// </summary>
    public static bool CanTransition(VerificationState from, VerificationState to, VerificationActorKind actor)
    {
        // Terminal sources are always blocked.
        if (from.IsTerminal())
        {
            return false;
        }

        // No state may transition back to `submitted`; resubmission creates a new row.
        if (to == VerificationState.Submitted)
        {
            return false;
        }

        return (from, to, actor) switch
        {
            // Reviewer pickup
            (VerificationState.Submitted, VerificationState.InReview, VerificationActorKind.Reviewer) => true,

            // Reviewer decisions from in-review
            (VerificationState.InReview, VerificationState.Approved, VerificationActorKind.Reviewer) => true,
            (VerificationState.InReview, VerificationState.Rejected, VerificationActorKind.Reviewer) => true,
            (VerificationState.InReview, VerificationState.InfoRequested, VerificationActorKind.Reviewer) => true,

            // Reviewer "skip explicit begin-review" path: same outcomes from `submitted`
            (VerificationState.Submitted, VerificationState.Approved, VerificationActorKind.Reviewer) => true,
            (VerificationState.Submitted, VerificationState.Rejected, VerificationActorKind.Reviewer) => true,
            (VerificationState.Submitted, VerificationState.InfoRequested, VerificationActorKind.Reviewer) => true,

            // Customer resubmits after info-requested. Must pass through in-review (FR-016 path).
            (VerificationState.InfoRequested, VerificationState.InReview, VerificationActorKind.Customer) => true,

            // System: expiry worker
            (VerificationState.Approved, VerificationState.Expired, VerificationActorKind.System) => true,

            // Reviewer revokes an active approval
            (VerificationState.Approved, VerificationState.Revoked, VerificationActorKind.Reviewer) => true,

            // System: renewal supersedes the prior approval atomically
            (VerificationState.Approved, VerificationState.Superseded, VerificationActorKind.System) => true,

            // System: account-lifecycle voids any non-terminal
            (_, VerificationState.Void, VerificationActorKind.System) when !from.IsTerminal() => true,

            _ => false,
        };
    }

    /// <summary>
    /// Throws <see cref="InvalidVerificationTransitionException"/> if the edge is
    /// not permitted; caller maps the exception to
    /// <see cref="VerificationReasonCode.InvalidStateForAction"/>.
    /// </summary>
    public static void EnsureCanTransitionOrThrow(
        VerificationState from,
        VerificationState to,
        VerificationActorKind actor)
    {
        if (!CanTransition(from, to, actor))
        {
            throw new InvalidVerificationTransitionException(from, to, actor);
        }
    }
}

public sealed class InvalidVerificationTransitionException : InvalidOperationException
{
    public InvalidVerificationTransitionException(
        VerificationState from,
        VerificationState to,
        VerificationActorKind actor)
        : base($"Verification state transition '{from.ToWireValue()}' → '{to.ToWireValue()}' is not allowed for actor '{actor.ToWireValue()}'.")
    {
        From = from;
        To = to;
        Actor = actor;
    }

    public VerificationState From { get; }
    public VerificationState To { get; }
    public VerificationActorKind Actor { get; }
}
