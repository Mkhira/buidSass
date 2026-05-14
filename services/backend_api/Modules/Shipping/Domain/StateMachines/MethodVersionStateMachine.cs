using BackendApi.Modules.Shipping.Primitives;

namespace BackendApi.Modules.Shipping.Domain.StateMachines;

/// <summary>
/// Lifecycle graph for <see cref="ShippingMethodVersion"/>.
/// <c>draft → in_review → published → archived</c>. Versions of the same
/// method auto-archive when a new one publishes (Flow 4 in spec.md).
/// V-1 publish gate enforced by the handler, not this graph.
/// </summary>
public static class MethodVersionStateMachine
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [MethodVersionStates.Draft] = Set(MethodVersionStates.InReview, MethodVersionStates.Archived),
            [MethodVersionStates.InReview] = Set(MethodVersionStates.Published, MethodVersionStates.Draft, MethodVersionStates.Archived),
            [MethodVersionStates.Published] = Set(MethodVersionStates.Archived),
            [MethodVersionStates.Archived] = Set(),
        };

    public static bool CanTransition(string from, string to) =>
        Transitions.TryGetValue(from, out var set) && set.Contains(to);

    public static void EnsureTransition(string from, string to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException(
                $"MethodVersion state transition not allowed: '{from}' → '{to}'.");
        }
    }

    private static IReadOnlySet<string> Set(params string[] members) =>
        new HashSet<string>(members, StringComparer.Ordinal);
}
