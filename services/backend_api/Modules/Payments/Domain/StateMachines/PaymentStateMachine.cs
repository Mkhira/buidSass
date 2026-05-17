using BackendApi.Modules.Payments.Primitives;

namespace BackendApi.Modules.Payments.Domain.StateMachines;

/// <summary>
/// Authoritative Payment.state transition graph per <c>data-model.md §state-machine</c>.
/// Used at write paths (provider response handler, webhook handler, operator
/// actions, refund cascades). Failing transitions raise
/// <see cref="InvalidOperationException"/> — there is no soft path.
/// </summary>
public static class PaymentStateMachine
{
    private static IReadOnlySet<string> Set(params string[] members) =>
        new HashSet<string>(members, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            // Synchronous card path (KSA Mada / Visa) — common path is straight to captured.
            [PaymentsConstants.PaymentStates.PendingAuthorization] = Set(
                PaymentsConstants.PaymentStates.Authorized,
                PaymentsConstants.PaymentStates.Captured,
                PaymentsConstants.PaymentStates.Failed,
                PaymentsConstants.PaymentStates.Expired),

            // Authorized → captured (or capture_failed for separate-capture flows).
            [PaymentsConstants.PaymentStates.Authorized] = Set(
                PaymentsConstants.PaymentStates.Captured,
                PaymentsConstants.PaymentStates.CaptureFailed,
                PaymentsConstants.PaymentStates.Failed,
                PaymentsConstants.PaymentStates.Expired),

            // capture_failed → captured (retry) | expired (24h auto-void).
            [PaymentsConstants.PaymentStates.CaptureFailed] = Set(
                PaymentsConstants.PaymentStates.Captured,
                PaymentsConstants.PaymentStates.Expired),

            // BNPL + some Apple Pay flows — redirect → webhook → captured/failed/expired.
            [PaymentsConstants.PaymentStates.PendingExternalRedirect] = Set(
                PaymentsConstants.PaymentStates.Captured,
                PaymentsConstants.PaymentStates.Failed,
                PaymentsConstants.PaymentStates.Expired),

            // COD — operator confirms cash receipt → captured | failed.
            [PaymentsConstants.PaymentStates.PendingCollectionOnDelivery] = Set(
                PaymentsConstants.PaymentStates.Captured,
                PaymentsConstants.PaymentStates.Failed),

            // Bank transfer — operator matches statement → captured | expired (72h).
            [PaymentsConstants.PaymentStates.PendingBankTransfer] = Set(
                PaymentsConstants.PaymentStates.Captured,
                PaymentsConstants.PaymentStates.Expired),

            // Post-capture refund / chargeback paths.
            [PaymentsConstants.PaymentStates.Captured] = Set(
                PaymentsConstants.PaymentStates.Refunded,
                PaymentsConstants.PaymentStates.PartiallyRefunded,
                PaymentsConstants.PaymentStates.ChargebackReceived),

            // partially_refunded → refunded (full) | partially_refunded (more partials) | chargeback.
            [PaymentsConstants.PaymentStates.PartiallyRefunded] = Set(
                PaymentsConstants.PaymentStates.PartiallyRefunded,
                PaymentsConstants.PaymentStates.Refunded,
                PaymentsConstants.PaymentStates.ChargebackReceived),

            // Terminal states.
            [PaymentsConstants.PaymentStates.Failed] = Set(),
            [PaymentsConstants.PaymentStates.Expired] = Set(),
            [PaymentsConstants.PaymentStates.Refunded] = Set(PaymentsConstants.PaymentStates.ChargebackReceived),
            [PaymentsConstants.PaymentStates.ChargebackReceived] = Set(),
        };

    public static bool CanTransition(string from, string to) =>
        Transitions.TryGetValue(from, out var set) && set.Contains(to);

    public static void EnsureTransition(string from, string to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException(
                $"Payment state transition not allowed: '{from}' → '{to}'.");
        }
    }

    /// <summary>The set of initial Payment.state values, indexed by method.</summary>
    public static string InitialStateForMethod(string method) => method switch
    {
        PaymentsConstants.Methods.Card => PaymentsConstants.PaymentStates.PendingAuthorization,
        PaymentsConstants.Methods.Mada => PaymentsConstants.PaymentStates.PendingAuthorization,
        PaymentsConstants.Methods.Meeza => PaymentsConstants.PaymentStates.PendingAuthorization,
        PaymentsConstants.Methods.ApplePay => PaymentsConstants.PaymentStates.PendingAuthorization,
        PaymentsConstants.Methods.StcPay => PaymentsConstants.PaymentStates.PendingAuthorization,
        PaymentsConstants.Methods.BnplTabby => PaymentsConstants.PaymentStates.PendingExternalRedirect,
        PaymentsConstants.Methods.BnplTamara => PaymentsConstants.PaymentStates.PendingExternalRedirect,
        PaymentsConstants.Methods.BnplValu => PaymentsConstants.PaymentStates.PendingExternalRedirect,
        PaymentsConstants.Methods.Cod => PaymentsConstants.PaymentStates.PendingCollectionOnDelivery,
        PaymentsConstants.Methods.BankTransfer => PaymentsConstants.PaymentStates.PendingBankTransfer,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown payment method."),
    };
}
