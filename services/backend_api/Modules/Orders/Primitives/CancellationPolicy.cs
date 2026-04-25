using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Primitives;

/// <summary>
/// FR-022 + research R5: per-market cancellation policy.
/// Cancel allowed iff: (no shipment exists) AND (
///   payment.state = authorized
///   OR (payment.state = captured AND now - placed_at &lt;= captured_cancel_hours)
///   OR (payment.state = pending_*  — pre-capture is always cancellable until shipment)
/// ).
///
/// Reads from <c>orders.cancellation_policies</c>; falls back to a conservative default if
/// the row is missing (KSA + EG seeded by Phase B B3, but defensive defaults guard against
/// a forgotten market or a yet-to-be-seeded environment).
/// </summary>
public sealed record CancellationDecision(bool Allowed, string? ReasonCode);

public sealed class CancellationPolicy(OrdersDbContext db)
{
    private const int DefaultCapturedCancelHours = 24;

    public async Task<CancellationDecision> EvaluateAsync(
        string marketCode,
        string paymentState,
        DateTimeOffset placedAt,
        bool shipmentExists,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        if (shipmentExists)
        {
            return new CancellationDecision(false, "order.cancel.shipment_exists");
        }

        var policy = await db.CancellationPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.MarketCode == marketCode, ct);
        var capturedCancelHours = policy?.CapturedCancelHours ?? DefaultCapturedCancelHours;
        var authorizedAllowed = policy?.AuthorizedCancelAllowed ?? true;

        // Pre-capture states are always cancellable (no money committed).
        if (string.Equals(paymentState, PaymentSm.Authorized, StringComparison.OrdinalIgnoreCase))
        {
            return authorizedAllowed
                ? new CancellationDecision(true, null)
                : new CancellationDecision(false, "order.cancel.policy_denied");
        }
        if (string.Equals(paymentState, PaymentSm.PendingCod, StringComparison.OrdinalIgnoreCase)
            || string.Equals(paymentState, PaymentSm.PendingBankTransfer, StringComparison.OrdinalIgnoreCase))
        {
            return new CancellationDecision(true, null);
        }

        // Captured: window-bounded (cancellation_pending → refund flow).
        if (string.Equals(paymentState, PaymentSm.Captured, StringComparison.OrdinalIgnoreCase))
        {
            var hoursSincePlaced = (nowUtc - placedAt).TotalHours;
            return hoursSincePlaced <= capturedCancelHours
                ? new CancellationDecision(true, null)
                : new CancellationDecision(false, "order.cancel.window_expired");
        }

        // Failed / voided / refunded: nothing to cancel.
        return new CancellationDecision(false, "order.cancel.policy_denied");
    }
}
