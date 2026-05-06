using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Lead.ReassignTicket;

/// <summary>
/// Lead reassign handler — supersedes the prior active assignment, creates a
/// new active assignment row, updates the denormalized
/// <see cref="SupportTicket.AssignedAgentId"/> cache, and emits the
/// <see cref="TicketReassigned"/> event.
///
/// <para>Per FR-008/FR-014a, a ≥ 10-character justification note is mandatory
/// and audited. Per FR-039, callers are subject to a 30/h rate limit which is
/// enforced at the HTTP layer.</para>
/// </summary>
public sealed class ReassignTicketHandler
{
    public const int MinimumJustificationLength = 10;

    private readonly SupportDbContext _db;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public ReassignTicketHandler(SupportDbContext db, IPublisher publisher, TimeProvider clock)
    {
        _db = db;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<ReassignTicketResult> HandleAsync(ReassignTicketCommand cmd, CancellationToken ct)
    {
        if (cmd.JustificationNote is null
            || cmd.JustificationNote.Trim().Length < MinimumJustificationLength)
        {
            return Failure(TicketReasonCode.ReassignJustificationRequired,
                $"Justification note must be at least {MinimumJustificationLength} characters.");
        }

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null)
        {
            return Failure(TicketReasonCode.LinkedEntityNotFound, "Ticket not found.");
        }

        if (ticket.State == TicketStateNames.Closed)
        {
            return Failure(TicketReasonCode.ClosedTerminal,
                "Cannot reassign a closed ticket.");
        }

        if (ticket.AssignedAgentId == cmd.TargetAgentId)
        {
            // Idempotent no-op — already pointed at this agent.
            return new ReassignTicketResult(
                Success: true,
                AssignmentId: null,
                PriorAgentId: cmd.TargetAgentId,
                ReasonCode: null,
                Detail: "Ticket is already assigned to the target agent.");
        }

        var nowUtc = _clock.GetUtcNow();
        var priorAgentId = ticket.AssignedAgentId;

        // Supersede any active prior assignment row(s) — partial unique index
        // permits at most one, but we sweep defensively.
        var prior = await _db.Assignments
            .Where(a => a.TicketId == ticket.Id && a.SupersededAtUtc == null)
            .ToListAsync(ct);
        foreach (var p in prior)
        {
            p.SupersededAtUtc = nowUtc;
            p.SupersededReason = TicketAssignmentSupersededReason.Reassigned;
        }

        var assignmentId = Guid.NewGuid();
        _db.Assignments.Add(new TicketAssignment
        {
            Id = assignmentId,
            TicketId = ticket.Id,
            AgentId = cmd.TargetAgentId,
            AssignmentKind = TicketAssignmentKind.LeadReassignment,
            AssignedByActorId = cmd.LeadActorId,
            JustificationNote = cmd.JustificationNote.Trim(),
            AssignedAtUtc = nowUtc,
        });

        ticket.AssignedAgentId = cmd.TargetAgentId;
        ticket.UpdatedAtUtc = nowUtc;

        // If the ticket was open, the reassignment also moves it to in_progress.
        var fromState = TicketStateNames.FromWire(ticket.State);
        var transitioned = false;
        if (fromState == TicketState.Open
            && TicketStateMachine.TryTransition(
                fromState, TicketState.InProgress,
                TicketTriggerKind.AgentAssignment,
                TicketActorKind.Lead,
                out _))
        {
            ticket.State = TicketStateNames.ToWire(TicketState.InProgress);
            transitioned = true;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505")
        {
            // Partial unique index UX_ticket_assignments_active_per_ticket race —
            // a parallel reassign or claim won. Treat as conflict.
            return Failure(TicketReasonCode.AssignmentConflict,
                "Ticket assignment changed concurrently; reload before retrying.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(TicketReasonCode.VersionConflict,
                "Ticket was modified concurrently; retry the request.");
        }

        await _publisher.Publish(new TicketReassigned(
            TicketId: ticket.Id,
            PriorAgentId: priorAgentId,
            NewAgentId: cmd.TargetAgentId,
            JustificationNote: cmd.JustificationNote.Trim(),
            OccurredAtUtc: nowUtc), ct);

        await _publisher.Publish(new TicketAssigned(
            TicketId: ticket.Id,
            AgentId: cmd.TargetAgentId,
            AssignmentKind: TicketAssignmentKind.LeadReassignment,
            OccurredAtUtc: nowUtc), ct);

        if (transitioned)
        {
            await _publisher.Publish(new TicketStateChanged(
                TicketId: ticket.Id,
                FromState: TicketStateNames.ToWire(fromState),
                ToState: TicketStateNames.ToWire(TicketState.InProgress),
                TriggeredBy: TicketTriggerKind.AgentAssignment,
                OccurredAtUtc: nowUtc), ct);
        }

        return new ReassignTicketResult(
            Success: true,
            AssignmentId: assignmentId,
            PriorAgentId: priorAgentId,
            ReasonCode: null,
            Detail: null);
    }

    private static ReassignTicketResult Failure(string reasonCode, string detail) =>
        new(Success: false, AssignmentId: null, PriorAgentId: null,
            ReasonCode: reasonCode, Detail: detail);
}
