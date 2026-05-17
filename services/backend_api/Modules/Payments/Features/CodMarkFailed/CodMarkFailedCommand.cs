using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Payments.Features.CodMarkFailed;

/// <summary>T035 — warehouse-operator marks COD as refused / address-not-found.</summary>
public sealed record CodMarkFailedCommand(Guid PaymentId, Guid OperatorId, string Outcome) : IRequest;

public sealed class CodMarkFailedHandler : IRequestHandler<CodMarkFailedCommand>
{
    private readonly PaymentsDbContext _db;
    private readonly PaymentTransitionService _transitions;
    private readonly TimeProvider _clock;

    public CodMarkFailedHandler(PaymentsDbContext db, PaymentTransitionService transitions, TimeProvider clock)
    {
        _db = db;
        _transitions = transitions;
        _clock = clock;
    }

    public async Task Handle(CodMarkFailedCommand cmd, CancellationToken ct)
    {
        if (!PaymentsConstants.CodOutcomes.All.Contains(cmd.Outcome)
            || cmd.Outcome == PaymentsConstants.CodOutcomes.Collected)
        {
            throw new InvalidOperationException("Outcome must be 'refused' or 'address_not_found'");
        }
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
        log.OperatorConfirmedAt = _clock.GetUtcNow();
        log.OperatorId = cmd.OperatorId;
        log.Outcome = cmd.Outcome;
        log.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        await _transitions.MarkFailedAsync(_db, payment, PaymentsConstants.FailedReasons.CodCollectionFailed, ct);
    }
}
