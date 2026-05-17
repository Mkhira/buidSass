using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Payments.Domain.StateMachines;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Payments.Features.Reconciliation;

public sealed record ListReconciliationRunsQuery(int Take = 50)
    : IRequest<IReadOnlyList<ReconciliationRunDto>>;

public sealed record ReconciliationRunDto(
    Guid Id, DateTime StartedAt, DateTime? CompletedAt, DateOnly DateRangeStart, DateOnly DateRangeEnd,
    int InternalPaymentsCount, int ProviderLedgerRowsCount, int MatchedCount, int ExceptionsCount, string Status);

public sealed class ListReconciliationRunsHandler : IRequestHandler<ListReconciliationRunsQuery, IReadOnlyList<ReconciliationRunDto>>
{
    private readonly PaymentsDbContext _db;
    public ListReconciliationRunsHandler(PaymentsDbContext db) { _db = db; }

    public async Task<IReadOnlyList<ReconciliationRunDto>> Handle(ListReconciliationRunsQuery q, CancellationToken ct)
    {
        return await _db.ReconciliationRuns
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(q.Take, 1, 200))
            .Select(r => new ReconciliationRunDto(
                r.Id, r.StartedAt.UtcDateTime, r.CompletedAt.HasValue ? r.CompletedAt.Value.UtcDateTime : null,
                r.DateRangeStart, r.DateRangeEnd,
                r.InternalPaymentsCount, r.ProviderLedgerRowsCount, r.MatchedCount, r.ExceptionsCount, r.Status))
            .ToListAsync(ct);
    }
}

public sealed record ListReconciliationExceptionsQuery(string? StateFilter = null, int Take = 100)
    : IRequest<IReadOnlyList<ReconciliationExceptionDto>>;

public sealed record ReconciliationExceptionDto(
    Guid Id, Guid RunId, string Reason, string? ProviderId, Guid? InternalPaymentId,
    decimal? InternalAmount, decimal? ProviderAmount, string State, string? Resolution, DateTime CreatedAt);

public sealed class ListReconciliationExceptionsHandler : IRequestHandler<ListReconciliationExceptionsQuery, IReadOnlyList<ReconciliationExceptionDto>>
{
    private readonly PaymentsDbContext _db;
    public ListReconciliationExceptionsHandler(PaymentsDbContext db) { _db = db; }

    public async Task<IReadOnlyList<ReconciliationExceptionDto>> Handle(ListReconciliationExceptionsQuery q, CancellationToken ct)
    {
        var query = _db.ReconciliationExceptions.AsQueryable().Where(e => e.DeletedAt == null);
        if (!string.IsNullOrEmpty(q.StateFilter))
            query = query.Where(e => e.State == q.StateFilter);
        return await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(Math.Clamp(q.Take, 1, 500))
            .Select(e => new ReconciliationExceptionDto(
                e.Id, e.RunId, e.Reason, e.ProviderId, e.InternalPaymentId,
                e.InternalAmount, e.ProviderAmount, e.State, e.Resolution, e.CreatedAt.UtcDateTime))
            .ToListAsync(ct);
    }
}

public sealed record ResolveReconciliationExceptionCommand(
    Guid ExceptionId, string Action, string? Notes, Guid OperatorId) : IRequest;

public sealed class ResolveReconciliationExceptionHandler : IRequestHandler<ResolveReconciliationExceptionCommand>
{
    private readonly PaymentsDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public ResolveReconciliationExceptionHandler(PaymentsDbContext db, IAuditEventPublisher audit, TimeProvider clock)
    {
        _db = db; _audit = audit; _clock = clock;
    }

    public async Task Handle(ResolveReconciliationExceptionCommand cmd, CancellationToken ct)
    {
        if (!ReconciliationExceptionStateMachine.IsValidResolution(cmd.Action))
            throw new InvalidOperationException("Action must be one of: refund_issued, internal_correction, provider_correction_requested, accepted_loss");
        var ex = await _db.ReconciliationExceptions.FirstOrDefaultAsync(
            e => e.Id == cmd.ExceptionId && e.DeletedAt == null, ct)
            ?? throw new InvalidOperationException("Exception not found");
        ReconciliationExceptionStateMachine.EnsureTransition(ex.State, PaymentsConstants.ReconciliationExceptionStates.Resolved);
        ex.State = PaymentsConstants.ReconciliationExceptionStates.Resolved;
        ex.Resolution = cmd.Action;
        ex.ResolutionNotes = cmd.Notes;
        ex.ResolvedBy = cmd.OperatorId;
        ex.ResolvedAt = _clock.GetUtcNow();
        ex.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        await _audit.PublishAsync(new AuditEvent(
            ActorId: cmd.OperatorId, ActorRole: "payments-operator",
            Action: PaymentsConstants.AuditActions.ReconciliationExceptionResolved,
            EntityType: "ReconciliationException", EntityId: ex.Id,
            BeforeState: null, AfterState: new { action = cmd.Action, notes = cmd.Notes },
            Reason: null), ct);
    }
}
