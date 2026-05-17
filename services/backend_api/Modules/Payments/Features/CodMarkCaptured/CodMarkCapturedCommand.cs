using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Payments.Features.CodMarkCaptured;

/// <summary>T035 — warehouse-operator marks COD payment as cash-received (AC-18).</summary>
public sealed record CodMarkCapturedCommand(Guid PaymentId, Guid OperatorId, decimal AmountCollected) : IRequest;

public sealed class CodMarkCapturedHandler : IRequestHandler<CodMarkCapturedCommand>
{
    private readonly PaymentsDbContext _db;
    private readonly PaymentTransitionService _transitions;
    private readonly TimeProvider _clock;

    public CodMarkCapturedHandler(PaymentsDbContext db, PaymentTransitionService transitions, TimeProvider clock)
    {
        _db = db;
        _transitions = transitions;
        _clock = clock;
    }

    public async Task Handle(CodMarkCapturedCommand cmd, CancellationToken ct)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p =>
            p.Id == cmd.PaymentId
            && p.DeletedAt == null
            && p.Method == PaymentsConstants.Methods.Cod, ct)
            ?? throw new InvalidOperationException("COD payment not found");
        if (payment.State != PaymentsConstants.PaymentStates.PendingCollectionOnDelivery)
        {
            throw new InvalidOperationException("COD payment is not in pending_collection_on_delivery");
        }
        var log = await _db.CodCollectionLogs.FirstOrDefaultAsync(c => c.PaymentId == cmd.PaymentId, ct);
        if (log is null)
        {
            log = new Domain.CodCollectionLog { PaymentId = cmd.PaymentId };
            _db.CodCollectionLogs.Add(log);
        }
        log.AmountCollected = cmd.AmountCollected;
        log.CollectedAt = _clock.GetUtcNow();
        log.OperatorConfirmedAt = _clock.GetUtcNow();
        log.OperatorId = cmd.OperatorId;
        log.Outcome = PaymentsConstants.CodOutcomes.Collected;
        log.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        await _transitions.CaptureAsync(_db, payment, ct);
    }
}
