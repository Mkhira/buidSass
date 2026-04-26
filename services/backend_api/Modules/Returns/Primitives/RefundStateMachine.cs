namespace BackendApi.Modules.Returns.Primitives;

/// <summary>
/// SM-2. Refund state machine — gateway-level lifecycle within Returns module. Distinct from
/// spec 011's <c>RefundSm</c> (which models the order-level refund_state of "none|requested|
/// partial|full"); this one tracks a single Refund row's gateway lifecycle.
/// </summary>
public static class RefundStateMachine
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string PendingManualTransfer = "pending_manual_transfer";
    public const string Completed = "completed";
    public const string Failed = "failed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Pending, InProgress, PendingManualTransfer, Completed, Failed,
    };

    public static bool IsValidTransition(string from, string to)
    {
        var f = from?.ToLowerInvariant() ?? string.Empty;
        var t = to?.ToLowerInvariant() ?? string.Empty;
        return (f, t) switch
        {
            (Pending, InProgress) => true,
            (Pending, PendingManualTransfer) => true,
            (InProgress, Completed) => true,
            (InProgress, Failed) => true,
            (PendingManualTransfer, Completed) => true,
            (Failed, InProgress) => true,
            (var ff, var tt) when ff == tt => true,
            _ => false,
        };
    }
}
