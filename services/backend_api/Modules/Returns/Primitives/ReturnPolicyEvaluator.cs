namespace BackendApi.Modules.Returns.Primitives;

/// <summary>
/// FR-001, FR-002. Evaluates whether a delivered order line is still inside the return window,
/// honouring per-product zero-window overrides for restricted/sealed-pharma items.
///
/// Inputs are passed in (no DB I/O) so callers can fold them into a single transaction with
/// the rest of the submit flow. Caller MUST resolve the policy + per-product flags via DI.
/// </summary>
public sealed class ReturnPolicyEvaluator
{
    public PolicyDecision Evaluate(PolicyEvaluationInput input)
    {
        if (input.DeliveredAt is null)
        {
            return PolicyDecision.Reject("return.order.not_delivered",
                "Order has not been delivered yet.");
        }
        if (input.ProductZeroWindow)
        {
            return PolicyDecision.Reject("return.line.restricted_zero_window",
                "Restricted product cannot be returned.");
        }
        if (input.ReturnWindowDays <= 0)
        {
            return PolicyDecision.Reject("return.window.expired",
                "Market return window is zero.");
        }
        // CR Minor: a future DeliveredAt produces a negative elapsed and silently passed
        // the window check, falsely accepting a not-yet-delivered line. Treat it the same
        // as not-delivered.
        if (input.DeliveredAt.Value > input.NowUtc)
        {
            return PolicyDecision.Reject("return.order.not_delivered",
                "Order has not been delivered yet.");
        }
        var elapsed = input.NowUtc - input.DeliveredAt.Value;
        if (elapsed.TotalDays > input.ReturnWindowDays)
        {
            return PolicyDecision.Reject("return.window.expired",
                $"Return window of {input.ReturnWindowDays} days has expired ({Math.Floor(elapsed.TotalDays)} days elapsed).");
        }
        return PolicyDecision.Accept();
    }
}

public sealed record PolicyEvaluationInput(
    DateTimeOffset? DeliveredAt,
    bool ProductZeroWindow,
    int ReturnWindowDays,
    DateTimeOffset NowUtc);

public sealed record PolicyDecision(bool Allowed, string? ReasonCode, string? Detail)
{
    public static PolicyDecision Accept() => new(true, null, null);
    public static PolicyDecision Reject(string reasonCode, string detail) => new(false, reasonCode, detail);
}
