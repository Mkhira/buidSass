using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Payments.Domain;
using BackendApi.Modules.Payments.Domain.StateMachines;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;

namespace BackendApi.Modules.Payments.Services;

/// <summary>
/// Single concentrated entry-point for Payment.state transitions. All
/// production code MUST route through here so that:
///  - the state machine guard is honored (PaymentStateMachine.EnsureTransition),
///  - the right timestamp column is populated,
///  - an audit row is emitted (BR-15 / AC-37),
///  - downstream subscribers (orders, invoice, notifications) see a single
///    well-formed event per transition.
/// </summary>
public sealed class PaymentTransitionService(
    IAuditEventPublisher auditPublisher,
    TimeProvider clock)
{
    public async Task CaptureAsync(PaymentsDbContext db, Payment payment, CancellationToken ct)
    {
        PaymentStateMachine.EnsureTransition(payment.State, PaymentsConstants.PaymentStates.Captured);
        var from = payment.State;
        payment.State = PaymentsConstants.PaymentStates.Captured;
        payment.CapturedAt = clock.GetUtcNow();
        payment.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await EmitAsync(payment, PaymentsConstants.AuditActions.PaymentCaptured, from, payment.State, ct);
    }

    public async Task MarkFailedAsync(PaymentsDbContext db, Payment payment, string reason, CancellationToken ct)
    {
        PaymentStateMachine.EnsureTransition(payment.State, PaymentsConstants.PaymentStates.Failed);
        var from = payment.State;
        payment.State = PaymentsConstants.PaymentStates.Failed;
        payment.FailedReason = reason;
        payment.FailedAt = clock.GetUtcNow();
        payment.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await EmitAsync(payment, PaymentsConstants.AuditActions.PaymentFailed, from, payment.State, ct);
    }

    public async Task MarkExpiredAsync(PaymentsDbContext db, Payment payment, string reason, CancellationToken ct)
    {
        PaymentStateMachine.EnsureTransition(payment.State, PaymentsConstants.PaymentStates.Expired);
        var from = payment.State;
        payment.State = PaymentsConstants.PaymentStates.Expired;
        payment.ExpiredReason = reason;
        payment.ExpiredAt = clock.GetUtcNow();
        payment.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await EmitAsync(payment, PaymentsConstants.AuditActions.PaymentExpired, from, payment.State, ct);
    }

    public async Task MarkPartiallyRefundedAsync(PaymentsDbContext db, Payment payment, decimal newSum, CancellationToken ct)
    {
        // Allowed transitions: captured → partially_refunded | refunded;
        // partially_refunded → partially_refunded | refunded.
        var target = newSum >= payment.Amount
            ? PaymentsConstants.PaymentStates.Refunded
            : PaymentsConstants.PaymentStates.PartiallyRefunded;
        PaymentStateMachine.EnsureTransition(payment.State, target);
        var from = payment.State;
        payment.State = target;
        payment.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        var action = target == PaymentsConstants.PaymentStates.Refunded
            ? PaymentsConstants.AuditActions.PaymentRefunded
            : PaymentsConstants.AuditActions.PaymentPartiallyRefunded;
        await EmitAsync(payment, action, from, payment.State, ct);
    }

    public async Task MarkChargebackAsync(PaymentsDbContext db, Payment payment, CancellationToken ct)
    {
        PaymentStateMachine.EnsureTransition(payment.State, PaymentsConstants.PaymentStates.ChargebackReceived);
        var from = payment.State;
        payment.State = PaymentsConstants.PaymentStates.ChargebackReceived;
        payment.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await EmitAsync(payment, PaymentsConstants.AuditActions.PaymentChargeback, from, payment.State, ct);
    }

    private Task EmitAsync(Payment payment, string action, string before, string after, CancellationToken ct) =>
        auditPublisher.PublishAsync(new AuditEvent(
            ActorId: Guid.Empty, ActorRole: "system",
            Action: action, EntityType: "Payment", EntityId: payment.Id,
            BeforeState: new { state = before },
            AfterState: new { state = after, amount = payment.Amount, currency = payment.Currency },
            Reason: payment.FailedReason ?? payment.ExpiredReason), ct);
}
