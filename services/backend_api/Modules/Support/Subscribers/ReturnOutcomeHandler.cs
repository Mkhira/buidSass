using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Subscribers;

/// <summary>
/// Spec 013 publishes <c>return.completed</c> / <c>return.rejected</c>;
/// this subscriber transitions the originating ticket to <c>resolved</c>
/// (FR-028 reverse linkage) and appends a system-event message summarising
/// the outcome. Idempotent — a redelivered event finds the ticket already
/// resolved and short-circuits.
/// </summary>
public sealed class ReturnOutcomeHandler : IReturnOutcomeSubscriber
{
    private readonly SupportDbContext _db;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public ReturnOutcomeHandler(SupportDbContext db, IPublisher publisher, TimeProvider clock)
    {
        _db = db;
        _publisher = publisher;
        _clock = clock;
    }

    public Task OnReturnCompletedAsync(ReturnCompletedNotice notice, CancellationToken ct) =>
        ProcessOutcomeAsync(notice.OriginatingTicketId, notice.ReturnRequestId, "completed", ct);

    public Task OnReturnRejectedAsync(ReturnRejectedNotice notice, CancellationToken ct) =>
        ProcessOutcomeAsync(notice.OriginatingTicketId, notice.ReturnRequestId, "rejected", ct);

    private async Task ProcessOutcomeAsync(
        Guid? originatingTicketId, Guid returnRequestId, string outcome, CancellationToken ct)
    {
        // Locate the originating ticket either via the explicit notice payload or via
        // back-traversal of the ticket_links table.
        SupportTicket? ticket = null;
        if (originatingTicketId is not null)
        {
            ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == originatingTicketId, ct);
        }
        else
        {
            var link = await _db.Links.AsNoTracking()
                .Where(l => l.LinkedEntityId == returnRequestId
                    && l.Kind == TicketLinkedEntityKindNames.ReturnRequest)
                .OrderByDescending(l => l.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);
            if (link is not null)
            {
                ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == link.TicketId, ct);
            }
        }

        if (ticket is null)
        {
            // Idempotency: if there's no originating ticket, the event is a no-op.
            return;
        }

        // Already terminal — idempotent no-op.
        if (ticket.State == TicketStateNames.Resolved || ticket.State == TicketStateNames.Closed)
        {
            return;
        }

        var fromState = TicketStateNames.FromWire(ticket.State);
        if (!TicketStateMachine.TryTransition(
                fromState, TicketState.Resolved,
                TicketTriggerKind.ReturnOutcome,
                TicketActorKind.System,
                out _))
        {
            return; // unsupported source state — leave alone.
        }

        var nowUtc = _clock.GetUtcNow();
        ticket.State = TicketStateNames.ToWire(TicketState.Resolved);
        ticket.ResolvedAtUtc = nowUtc;
        ticket.UpdatedAtUtc = nowUtc;

        _db.Messages.Add(new TicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Kind = TicketMessageKindNames.SystemEvent,
            ActorId = null,
            ActorRole = TicketActorKindNames.System,
            // Capture the outcome in the message body so the audit trail
            // preserves whether the return was completed or rejected.
            Body = $"Return request {outcome}.",
            BodyLocale = ticket.Locale,
            LeadIntervention = false,
            CreatedAtUtc = nowUtc,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Race lost — another concurrent transition already moved the ticket.
            return;
        }

        await _publisher.Publish(new TicketReturnOutcomeReceived(
            TicketId: ticket.Id,
            ReturnRequestId: returnRequestId,
            Outcome: outcome,
            OccurredAtUtc: nowUtc), ct);

        await _publisher.Publish(new TicketStateChanged(
            TicketId: ticket.Id,
            FromState: TicketStateNames.ToWire(fromState),
            ToState: ticket.State,
            TriggeredBy: TicketTriggerKind.ReturnOutcome,
            OccurredAtUtc: nowUtc), ct);

        // System-triggered resolution — no human agent performed the action,
        // so ResolvedByAgentId is null. The originating return-request is
        // already captured in TicketReturnOutcomeReceived above.
        await _publisher.Publish(new TicketResolved(
            TicketId: ticket.Id,
            ResolvedByAgentId: null,
            ResolvedAtUtc: nowUtc), ct);
    }
}
