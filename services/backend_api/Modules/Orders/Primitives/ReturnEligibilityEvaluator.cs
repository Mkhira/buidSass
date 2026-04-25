using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Primitives.StateMachines;

namespace BackendApi.Modules.Orders.Primitives;

/// <summary>
/// FR-009 + research R11. Returns whether an order is within the return-eligibility window.
///
/// SOURCE OF TRUTH: spec 013 owns <c>returns.return_policies.return_window_days</c> per market.
/// Spec 013 has not landed at the time spec 011 ships, so this evaluator currently uses
/// hard-coded launch defaults from R11 (KSA = 14 days, EG = 7 days). When spec 013 lands it
/// MUST replace this static table with a cross-schema read. The constants below reference
/// R11 in their xmldoc so the dependency is visible to the compiler-time reader.
/// </summary>
public sealed record ReturnEligibility(bool Eligible, int? DaysRemaining, string? ReasonCode);

public sealed class ReturnEligibilityEvaluator
{
    /// <summary>R11 launch defaults; spec 013 replaces this with a DB read.</summary>
    private static readonly IReadOnlyDictionary<string, int> LaunchWindowDays =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["KSA"] = 14,
            ["EG"] = 7,
        };

    public ReturnEligibility Evaluate(Order order, DateTimeOffset nowUtc)
    {
        if (!string.Equals(order.FulfillmentState, FulfillmentSm.Delivered, StringComparison.OrdinalIgnoreCase))
        {
            return new ReturnEligibility(false, null, "order.return.not_delivered");
        }
        if (order.DeliveredAt is null)
        {
            return new ReturnEligibility(false, null, "order.return.not_delivered");
        }
        var windowDays = LaunchWindowDays.TryGetValue(order.MarketCode, out var d) ? d : 14;
        var daysSince = (int)Math.Floor((nowUtc - order.DeliveredAt.Value).TotalDays);
        var daysRemaining = windowDays - daysSince;
        if (daysRemaining < 0)
        {
            return new ReturnEligibility(false, 0, "returnWindow.expired");
        }
        return new ReturnEligibility(true, daysRemaining, null);
    }
}
