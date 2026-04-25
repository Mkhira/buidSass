namespace BackendApi.Modules.Orders.Primitives.StateMachines;

/// <summary>
/// Refund state machine. Spec 011 SM-4. Spec 013 owns the deeper return/refund semantics —
/// this machine is a seam advanced by spec 013's <c>returns_outbox</c> dispatcher via the
/// <c>POST /v1/internal/orders/{id}/advance-refund-state</c> endpoint (contract orders-contract.md).
/// </summary>
public static class RefundSm
{
    public const string None = "none";
    public const string Requested = "requested";
    public const string Partial = "partial";
    public const string Full = "full";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        None, Requested, Partial, Full,
    };

    public static bool IsValidTransition(string from, string to)
    {
        // CR review round 1: normalize at the boundary so explicit tuple arms behave
        // consistently with the case-insensitive `All` set. A caller passing "REQUESTED" /
        // "FULL" is now accepted (was previously rejected by the case-sensitive switch).
        var f = from?.ToLowerInvariant() ?? string.Empty;
        var t = to?.ToLowerInvariant() ?? string.Empty;
        return (f, t) switch
        {
            (None, Requested) => true,                    // spec 013 return submitted
            (Requested, None) => true,                    // return rejected with no other open RMA
            (Requested, Partial) => true,                 // partial refund issued
            (Requested, Full) => true,                    // full refund issued
            (Partial, Full) => true,                      // subsequent refund covers remainder
            // Idempotent self-transition absorbs duplicate dispatcher deliveries.
            (var ff, var tt) when ff == tt => true,
            _ => false,
        };
    }
}
