namespace BackendApi.Modules.Cart.Primitives;

/// <summary>
/// Combines restriction + inventory + B2B prerequisites into a single
/// `checkoutEligibility` surface surfaced on every cart read (FR-006).
/// Restricted products remain visible per Principle 8 — eligibility never hides the cart;
/// it only gates the *checkout* attempt.
/// </summary>
public sealed class EligibilityEvaluator
{
    public sealed record Input(
        bool HasAnyRestrictedLine,
        string? RestrictedReasonCode,
        bool CustomerVerifiedForRestriction,
        bool HasAnyUnavailableLine,
        bool HasAnyInventoryShortfall,
        bool HasAnyB2BOnlyLine,
        bool CustomerIsB2B,
        int LineCount);

    public sealed record Result(bool Allowed, string? ReasonCode);

    public Result Evaluate(Input input)
    {
        if (input.LineCount == 0)
        {
            return new Result(false, "cart.empty");
        }
        if (input.HasAnyUnavailableLine)
        {
            return new Result(false, "cart.line_unavailable");
        }
        if (input.HasAnyInventoryShortfall)
        {
            return new Result(false, "cart.inventory_insufficient");
        }
        if (input.HasAnyRestrictedLine && !input.CustomerVerifiedForRestriction)
        {
            return new Result(false, input.RestrictedReasonCode ?? "catalog.restricted.verification_required");
        }
        if (input.HasAnyB2BOnlyLine && !input.CustomerIsB2B)
        {
            return new Result(false, "cart.b2b_required");
        }
        return new Result(true, null);
    }
}
