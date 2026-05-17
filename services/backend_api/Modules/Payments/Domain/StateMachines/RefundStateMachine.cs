using BackendApi.Modules.Payments.Primitives;

namespace BackendApi.Modules.Payments.Domain.StateMachines;

/// <summary>
/// Refund.state graph per <c>data-model.md §Refund.state</c>:
/// <c>pending → completed | failed</c>. Independent of Payment.state to keep
/// refund execution decoupled from payment capture (Principle 17).
/// </summary>
public static class RefundStateMachine
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [PaymentsConstants.RefundStates.Pending] = new HashSet<string>(StringComparer.Ordinal)
            {
                PaymentsConstants.RefundStates.Completed,
                PaymentsConstants.RefundStates.Failed,
            },
            [PaymentsConstants.RefundStates.Completed] = new HashSet<string>(StringComparer.Ordinal),
            [PaymentsConstants.RefundStates.Failed] = new HashSet<string>(StringComparer.Ordinal),
        };

    public static bool CanTransition(string from, string to) =>
        Transitions.TryGetValue(from, out var set) && set.Contains(to);

    public static void EnsureTransition(string from, string to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException(
                $"Refund state transition not allowed: '{from}' → '{to}'.");
        }
    }
}
