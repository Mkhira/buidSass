using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Payments.Features.Chargebacks;

public sealed record ListChargebacksQuery(int Take = 100)
    : IRequest<IReadOnlyList<ChargebackDto>>;

public sealed record ChargebackDto(
    Guid Id, Guid PaymentId, string ProviderChargebackId, decimal Amount,
    string? ReasonCode, string Status, DateTime ReceivedAt);

public sealed class ListChargebacksHandler : IRequestHandler<ListChargebacksQuery, IReadOnlyList<ChargebackDto>>
{
    private readonly PaymentsDbContext _db;
    public ListChargebacksHandler(PaymentsDbContext db) { _db = db; }

    public async Task<IReadOnlyList<ChargebackDto>> Handle(ListChargebacksQuery q, CancellationToken ct)
    {
        return await _db.Chargebacks
            .Where(c => c.DeletedAt == null)
            .OrderByDescending(c => c.ReceivedAt)
            .Take(Math.Clamp(q.Take, 1, 500))
            .Select(c => new ChargebackDto(c.Id, c.PaymentId, c.ProviderChargebackId, c.Amount, c.ReasonCode, c.Status, c.ReceivedAt.UtcDateTime))
            .ToListAsync(ct);
    }
}

public sealed record UpdateChargebackCommand(Guid Id, string Status, string? ResolutionNotes, Guid OperatorId)
    : IRequest;

public sealed class UpdateChargebackHandler : IRequestHandler<UpdateChargebackCommand>
{
    private readonly PaymentsDbContext _db;
    private readonly TimeProvider _clock;

    public UpdateChargebackHandler(PaymentsDbContext db, TimeProvider clock) { _db = db; _clock = clock; }

    public async Task Handle(UpdateChargebackCommand cmd, CancellationToken ct)
    {
        if (!PaymentsConstants.ChargebackStatuses.All.Contains(cmd.Status))
            throw new InvalidOperationException("Invalid chargeback status");
        var cb = await _db.Chargebacks.FirstOrDefaultAsync(c => c.Id == cmd.Id && c.DeletedAt == null, ct)
            ?? throw new InvalidOperationException("Chargeback not found");
        cb.Status = cmd.Status;
        cb.ResolutionNotes = cmd.ResolutionNotes;
        cb.UpdatedAt = _clock.GetUtcNow();
        if (cmd.Status is PaymentsConstants.ChargebackStatuses.Won
            or PaymentsConstants.ChargebackStatuses.Lost
            or PaymentsConstants.ChargebackStatuses.Accepted)
        {
            cb.ResolvedAt = _clock.GetUtcNow();
            cb.ResolvedBy = cmd.OperatorId;
        }
        await _db.SaveChangesAsync(ct);
    }
}
