using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Payments.Features.BankTransferMarkCaptured;

/// <summary>T036 — payments-operator reconciles a bank-statement entry against a payment reference (AC-19).</summary>
public sealed record BankTransferMarkCapturedCommand(
    Guid PaymentId, Guid OperatorId, string BankStatementEntryJson) : IRequest;

public sealed class BankTransferMarkCapturedHandler : IRequestHandler<BankTransferMarkCapturedCommand>
{
    private readonly PaymentsDbContext _db;
    private readonly PaymentTransitionService _transitions;
    private readonly TimeProvider _clock;

    public BankTransferMarkCapturedHandler(
        PaymentsDbContext db, PaymentTransitionService transitions, TimeProvider clock)
    {
        _db = db;
        _transitions = transitions;
        _clock = clock;
    }

    public async Task Handle(BankTransferMarkCapturedCommand cmd, CancellationToken ct)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p =>
            p.Id == cmd.PaymentId
            && p.DeletedAt == null
            && p.Method == PaymentsConstants.Methods.BankTransfer, ct)
            ?? throw new InvalidOperationException("Bank-transfer payment not found");
        if (payment.State != PaymentsConstants.PaymentStates.PendingBankTransfer)
        {
            throw new InvalidOperationException("Bank-transfer payment is not in pending_bank_transfer");
        }
        var reference = await _db.BankTransferReferences.FirstOrDefaultAsync(
            r => r.PaymentId == payment.Id && r.DeletedAt == null, ct)
            ?? throw new InvalidOperationException("Bank-transfer reference not found");
        reference.MatchedBankStatementEntryJson = cmd.BankStatementEntryJson;
        reference.MatchedAt = _clock.GetUtcNow();
        reference.MatchedBy = cmd.OperatorId;
        reference.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        await _transitions.CaptureAsync(_db, payment, ct);
    }
}
