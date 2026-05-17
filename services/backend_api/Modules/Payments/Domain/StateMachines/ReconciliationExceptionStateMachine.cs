using BackendApi.Modules.Payments.Primitives;

namespace BackendApi.Modules.Payments.Domain.StateMachines;

/// <summary>
/// ReconciliationException.state graph: <c>open → resolved</c>. The
/// <c>resolution</c> column carries the documented action; AC-32 requires
/// the resolution to be one of the four canonical actions enumerated in
/// <see cref="PaymentsConstants.ReconciliationExceptionResolutions"/>.
/// </summary>
public static class ReconciliationExceptionStateMachine
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [PaymentsConstants.ReconciliationExceptionStates.Open] = new HashSet<string>(StringComparer.Ordinal)
            {
                PaymentsConstants.ReconciliationExceptionStates.Resolved,
            },
            [PaymentsConstants.ReconciliationExceptionStates.Resolved] = new HashSet<string>(StringComparer.Ordinal),
        };

    public static bool CanTransition(string from, string to) =>
        Transitions.TryGetValue(from, out var set) && set.Contains(to);

    public static void EnsureTransition(string from, string to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException(
                $"ReconciliationException state transition not allowed: '{from}' → '{to}'.");
        }
    }

    public static bool IsValidResolution(string? resolution) =>
        resolution is not null && PaymentsConstants.ReconciliationExceptionResolutions.All.Contains(resolution);
}
