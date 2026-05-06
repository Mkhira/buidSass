using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Lead.ForceCloseTicket;

/// <summary>
/// Spec 023 Phase 10 T122 — lead force-closes a ticket from any non-closed
/// state. Reason note ≥ 10 chars (FR-008-style audit trail). Idempotent on
/// already-closed tickets (returns the original closed_at_utc unchanged).
/// </summary>
public sealed class ForceCloseTicketHandler
{
    public const int MinimumReasonLength = 10;

    private readonly SupportDbContext _db;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public ForceCloseTicketHandler(SupportDbContext db, IPublisher publisher, TimeProvider clock)
    {
        _db = db;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<ForceCloseTicketResult> HandleAsync(ForceCloseTicketCommand cmd, CancellationToken ct)
    {
        if (cmd.ReasonNote is null
            || cmd.ReasonNote.Trim().Length < MinimumReasonLength)
        {
            return Failure(TicketReasonCode.ForceCloseReasonRequired,
                $"Reason note must be at least {MinimumReasonLength} characters.");
        }

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null)
        {
            return Failure(TicketReasonCode.LinkedEntityNotFound, "Ticket not found.");
        }

        if (ticket.State == TicketStateNames.Closed)
        {
            // Idempotent — already closed.
            return new ForceCloseTicketResult(
                Success: true,
                PriorState: TicketStateNames.Closed,
                ClosedAtUtc: ticket.ClosedAtUtc,
                ReasonCode: null,
                Detail: "Ticket was already closed.");
        }

        var fromState = TicketStateNames.FromWire(ticket.State);
        if (!TicketStateMachine.TryTransition(
                fromState, TicketState.Closed,
                TicketTriggerKind.LeadForceClose,
                TicketActorKind.Lead, out var reason))
        {
            return Failure(reason ?? TicketReasonCode.InvalidTransition,
                "Force-close transition rejected by state machine.");
        }

        var nowUtc = _clock.GetUtcNow();
        ticket.State = TicketStateNames.ToWire(TicketState.Closed);
        ticket.ClosedAtUtc = nowUtc;
        ticket.UpdatedAtUtc = nowUtc;

        _db.Messages.Add(new TicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Kind = TicketMessageKindNames.SystemEvent,
            ActorId = cmd.LeadActorId,
            ActorRole = TicketActorKindNames.Lead,
            Body = cmd.ReasonNote.Trim(),
            BodyLocale = ticket.Locale,
            LeadIntervention = true,
            CreatedAtUtc = nowUtc,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(TicketReasonCode.VersionConflict,
                "Ticket was modified concurrently; retry the request.");
        }

        await _publisher.Publish(new TicketStateChanged(
            TicketId: ticket.Id,
            FromState: TicketStateNames.ToWire(fromState),
            ToState: TicketStateNames.ToWire(TicketState.Closed),
            TriggeredBy: TicketTriggerKind.LeadForceClose,
            OccurredAtUtc: nowUtc), ct);

        await _publisher.Publish(new TicketClosed(
            TicketId: ticket.Id,
            TriggeredBy: TicketTriggerKind.LeadForceClose,
            ClosedAtUtc: nowUtc), ct);

        return new ForceCloseTicketResult(
            Success: true,
            PriorState: TicketStateNames.ToWire(fromState),
            ClosedAtUtc: nowUtc,
            ReasonCode: null,
            Detail: null);
    }

    private static ForceCloseTicketResult Failure(string reasonCode, string detail) =>
        new(false, null, null, reasonCode, detail);
}
