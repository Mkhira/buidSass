using BackendApi.Modules.Payments.Domain;
using BackendApi.Modules.Payments.Domain.StateMachines;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Providers;
using BackendApi.Modules.Payments.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Payments.Features.Refund;

/// <summary>
/// T040 — operator-initiated refund. Enforces V-5
/// (<c>SUM(refunds.amount) &lt;= payments.amount</c>) at the application
/// layer (defense-in-depth alongside the DB trigger). Refund.state advances
/// pending → completed | failed. On every completion, the parent Payment's
/// state may advance to <c>partially_refunded</c> or <c>refunded</c>.
/// </summary>
public sealed record RefundCommand(
    Guid PaymentId, decimal Amount, string? Reason, Guid InitiatedBy) : IRequest<RefundResponse>;

public sealed record RefundResponse(Guid RefundId, string State, string ParentPaymentState);

public sealed class RefundHandler : IRequestHandler<RefundCommand, RefundResponse>
{
    private readonly PaymentsDbContext _db;
    private readonly ProviderRegistry _providers;
    private readonly PaymentTransitionService _transitions;
    private readonly TimeProvider _clock;

    public RefundHandler(
        PaymentsDbContext db, ProviderRegistry providers,
        PaymentTransitionService transitions, TimeProvider clock)
    {
        _db = db;
        _providers = providers;
        _transitions = transitions;
        _clock = clock;
    }

    public async Task<RefundResponse> Handle(RefundCommand cmd, CancellationToken ct)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == cmd.PaymentId && p.DeletedAt == null, ct)
            ?? throw new InvalidOperationException("Payment not found");
        if (payment.State != PaymentsConstants.PaymentStates.Captured
            && payment.State != PaymentsConstants.PaymentStates.PartiallyRefunded)
        {
            throw new InvalidOperationException("Refunds are only allowed on captured / partially_refunded payments");
        }
        if (cmd.Amount <= 0)
        {
            throw new InvalidOperationException("Refund amount must be positive");
        }

        // V-5 — app-level pre-check (DB trigger is the second layer).
        var existingSum = await _db.Refunds
            .Where(r => r.PaymentId == payment.Id
                && r.DeletedAt == null
                && (r.State == PaymentsConstants.RefundStates.Pending
                    || r.State == PaymentsConstants.RefundStates.Completed))
            .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;
        var newSum = existingSum + cmd.Amount;
        if (newSum > payment.Amount)
        {
            throw new RefundExceedsCapturedAmountException(payment.Amount, existingSum, cmd.Amount);
        }

        var refund = new Domain.Refund
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Amount = cmd.Amount,
            Currency = payment.Currency,
            Reason = cmd.Reason,
            State = PaymentsConstants.RefundStates.Pending,
            InitiatedBy = cmd.InitiatedBy,
            CreatedAt = _clock.GetUtcNow(),
            UpdatedAt = _clock.GetUtcNow(),
        };
        _db.Refunds.Add(refund);
        await _db.SaveChangesAsync(ct);

        // For native methods (COD + bank transfer) there's no provider call;
        // operator-initiated refunds for those flows complete immediately.
        if (payment.ProviderId is null)
        {
            RefundStateMachine.EnsureTransition(refund.State, PaymentsConstants.RefundStates.Completed);
            refund.State = PaymentsConstants.RefundStates.Completed;
            refund.CompletedAt = _clock.GetUtcNow();
            refund.UpdatedAt = _clock.GetUtcNow();
            await _db.SaveChangesAsync(ct);
            await _transitions.MarkPartiallyRefundedAsync(_db, payment, newSum, ct);
            return new RefundResponse(refund.Id, refund.State, payment.State);
        }

        if (!_providers.TryResolve(payment.ProviderId, out var provider) || provider is null)
        {
            throw new InvalidOperationException($"Provider '{payment.ProviderId}' not registered");
        }

        var result = await provider.RefundAsync(new RefundDispatch(
            refund.Id, payment.ProviderMessageId ?? string.Empty, refund.Amount, refund.Currency, refund.Reason), ct);
        if (!result.Success)
        {
            RefundStateMachine.EnsureTransition(refund.State, PaymentsConstants.RefundStates.Failed);
            refund.State = PaymentsConstants.RefundStates.Failed;
            refund.FailedReason = result.FailureReason;
            refund.FailedAt = _clock.GetUtcNow();
            refund.UpdatedAt = _clock.GetUtcNow();
            await _db.SaveChangesAsync(ct);
            return new RefundResponse(refund.Id, refund.State, payment.State);
        }

        RefundStateMachine.EnsureTransition(refund.State, PaymentsConstants.RefundStates.Completed);
        refund.State = PaymentsConstants.RefundStates.Completed;
        refund.ProviderRefundId = result.ProviderRefundId;
        refund.CompletedAt = _clock.GetUtcNow();
        refund.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        await _transitions.MarkPartiallyRefundedAsync(_db, payment, newSum, ct);
        return new RefundResponse(refund.Id, refund.State, payment.State);
    }
}

public sealed class RefundExceedsCapturedAmountException : Exception
{
    public decimal Captured { get; }
    public decimal AlreadyRefunded { get; }
    public decimal RequestedNow { get; }

    public RefundExceedsCapturedAmountException(decimal captured, decimal alreadyRefunded, decimal requestedNow)
        : base($"Refund of {requestedNow} would push total ({alreadyRefunded + requestedNow}) above captured ({captured}).")
    {
        Captured = captured;
        AlreadyRefunded = alreadyRefunded;
        RequestedNow = requestedNow;
    }
}
